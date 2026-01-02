using System.Collections.Concurrent;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// One Session per endpoint. Prefer session recovery (SessionReconnectHandler) over rebuild.
/// Security (MVP): auto-trust server cert for internal networks.
/// </summary>
public sealed class OpcUaSessionManager : IAsyncDisposable
{
    private readonly IOptions<OpcUaOptions> _options;
    private readonly OpcUaHealthChecker _health;

    private readonly ConcurrentDictionary<string, ManagedSession> _sessions = new();
    private ApplicationConfiguration? _appConfig;
    private readonly SemaphoreSlim _init = new(1, 1);

    public OpcUaSessionManager(IOptions<OpcUaOptions> options, OpcUaHealthChecker health)
    {
        _options = options;
        _health = health;
    }

    public async Task<ISession> GetOrCreateSessionAsync(OpcUaEndpointConfig ep, CancellationToken ct)
    {
        await EnsureAppConfigAsync(ct).ConfigureAwait(false);

        var ms = _sessions.GetOrAdd(ep.EndpointId, _ => new ManagedSession(ep));
        return await ms.GetOrCreateAsync(_appConfig!, _health, ct).ConfigureAwait(false);
    }

    public int ActiveSessionCount => _sessions.Values.Count(s => s.IsOpen);

    public async Task CloseAllAsync(CancellationToken ct)
    {
        foreach (var s in _sessions.Values)
        {
            try { await s.CloseAsync(ct).ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAllAsync(CancellationToken.None).ConfigureAwait(false);
        _init.Dispose();
    }

    private async Task EnsureAppConfigAsync(CancellationToken ct)
    {
        if (_appConfig is not null) return;

        await _init.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_appConfig is not null) return;

            var cfg = new ApplicationConfiguration
            {
                ApplicationName = "IntelliMaint.Edge",
                ApplicationUri = $"urn:{Environment.MachineName}:IntelliMaint.Edge",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = "pki/own",
                        SubjectName = "CN=IntelliMaint.Edge"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/trusted"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/issuers"
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/rejected"
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 60000,
                    MaxStringLength = 1_000_000,
                    MaxByteStringLength = 1_000_000,
                    MaxArrayLength = 1_000_000,
                    MaxMessageSize = 4_000_000,
                    MaxBufferSize = 4_000_000,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    WellKnownDiscoveryUrls = new StringCollection()
                }
            };

            cfg.CertificateValidator = new CertificateValidator();
            cfg.CertificateValidator.CertificateValidation += (_, e) =>
            {
                // MVP: auto-trust (internal network). Production should pin trust.
                e.Accept = true;
            };

            await cfg.Validate(ApplicationType.Client).ConfigureAwait(false);

            _appConfig = cfg;
        }
        finally
        {
            _init.Release();
        }
    }

    private sealed class ManagedSession
    {
        private readonly OpcUaEndpointConfig _ep;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private ISession? _session;
        private SessionReconnectHandler? _reconnectHandler;
        private int _backoffStep = 0;
        private DateTimeOffset _nextConnectAllowedUtc = DateTimeOffset.MinValue;

        public ManagedSession(OpcUaEndpointConfig ep) => _ep = ep;

        public bool IsOpen => _session?.Connected == true;

        public async Task<ISession> GetOrCreateAsync(ApplicationConfiguration appCfg, OpcUaHealthChecker health, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_session?.Connected == true)
                    return _session;

                var now = DateTimeOffset.UtcNow;
                if (now < _nextConnectAllowedUtc)
                    throw new InvalidOperationException($"Reconnect backoff active until {_nextConnectAllowedUtc:O} for endpoint={_ep.EndpointId}");

                // Build endpoint
                var selectedEndpoint = CoreClientUtils.SelectEndpoint(_ep.EndpointUrl, useSecurity: !IsNoneSecurity(_ep));
                var endpointCfg = EndpointConfiguration.Create(appCfg);
                endpointCfg.OperationTimeout = _ep.SessionTimeoutMs;

                var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointCfg);

                IUserIdentity identity = BuildIdentity(_ep);
                
                // Create session - use the async overload
                var session = await Session.Create(
                    appCfg,
                    endpoint,
                    false,
                    $"IntelliMaint-{_ep.EndpointId}",
                    (uint)_ep.SessionTimeoutMs,
                    identity,
                    null).ConfigureAwait(false);

                // Setup keepalive handler
                session.KeepAlive += OnKeepAlive;

                // Reset backoff on success
                _backoffStep = 0;
                _nextConnectAllowedUtc = DateTimeOffset.MinValue;

                _session = session;
                health.MarkConnected(activeConnections: 0);

                return session;

                void OnKeepAlive(ISession s, KeepAliveEventArgs e)
                {
                    if (e.Status != null && ServiceResult.IsBad(e.Status))
                    {
                        BeginReconnect(s, health);
                    }
                }
            }
            catch (Exception ex)
            {
                _backoffStep = Math.Min(_backoffStep + 1, 6);
                _nextConnectAllowedUtc = DateTimeOffset.UtcNow.Add(BackoffDelay(_backoffStep));
                health.MarkDisconnected($"Session connect failed endpoint={_ep.EndpointId}: {ex.Message}");
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task CloseAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _reconnectHandler?.Dispose();
                _reconnectHandler = null;

                if (_session is not null)
                {
                    try { _session.Close(); } catch { }
                    _session.Dispose();
                    _session = null;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private void BeginReconnect(ISession session, OpcUaHealthChecker health)
        {
            try
            {
                if (_reconnectHandler is not null) return;

                _reconnectHandler = new SessionReconnectHandler();
                _reconnectHandler.BeginReconnect(session, 10_000, (sender, e) =>
                {
                    if (ReferenceEquals(sender, _reconnectHandler))
                    {
                        _session = _reconnectHandler.Session;
                        _reconnectHandler.Dispose();
                        _reconnectHandler = null;
                        health.MarkConnected(activeConnections: 0);
                    }
                });

                health.MarkDegraded($"Session keepalive bad endpoint={_ep.EndpointId}, reconnecting.");
            }
            catch (Exception ex)
            {
                health.MarkDisconnected($"Reconnect handler failed endpoint={_ep.EndpointId}: {ex.Message}");
            }
        }

        private static bool IsNoneSecurity(OpcUaEndpointConfig ep)
        {
            var mode = (ep.MessageSecurityMode ?? "None").Trim();
            return mode.Equals("None", StringComparison.OrdinalIgnoreCase)
                   || (ep.SecurityPolicy ?? "None").Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
        }

        private static IUserIdentity BuildIdentity(OpcUaEndpointConfig ep)
        {
            if (!string.IsNullOrWhiteSpace(ep.Username))
                return new UserIdentity(ep.Username, ep.Password ?? string.Empty);

            return new UserIdentity(new AnonymousIdentityToken());
        }

        private static TimeSpan BackoffDelay(int step) => step switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(2),
            3 => TimeSpan.FromSeconds(5),
            4 => TimeSpan.FromSeconds(10),
            5 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(60),
        };
    }
}
