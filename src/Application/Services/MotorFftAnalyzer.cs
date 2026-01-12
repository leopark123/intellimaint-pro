using System.Numerics;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v64: 电机 FFT 频域分析服务
/// 用于电流信号分析 (MCSA) 和轴承故障特征频率检测
/// </summary>
public sealed class MotorFftAnalyzer
{
    /// <summary>
    /// 执行 FFT 分析并提取频域特征
    /// </summary>
    /// <param name="samples">时域采样数据</param>
    /// <param name="sampleRate">采样率 (Hz)</param>
    /// <param name="motorParams">电机参数（用于计算特征频率）</param>
    /// <returns>频域分析结果</returns>
    public FftAnalysisResult Analyze(double[] samples, double sampleRate, MotorFftParams motorParams)
    {
        if (samples.Length == 0)
            return FftAnalysisResult.Empty;

        // 确保样本数为 2 的幂次方（FFT 要求）
        var paddedLength = NextPowerOfTwo(samples.Length);
        var paddedSamples = new Complex[paddedLength];
        for (int i = 0; i < samples.Length; i++)
            paddedSamples[i] = new Complex(samples[i], 0);

        // 应用汉宁窗减少频谱泄漏
        ApplyHanningWindow(paddedSamples, samples.Length);

        // 执行 FFT
        Fft(paddedSamples);

        // 计算幅度谱
        var freqResolution = sampleRate / paddedLength;
        var magnitudes = new double[paddedLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
            magnitudes[i] = paddedSamples[i].Magnitude * 2 / samples.Length;

        // 计算特征频率
        var fundamentalFreq = motorParams.SupplyFrequency;
        var rotationalFreq = motorParams.RotationalSpeed / 60.0; // RPM -> Hz

        // 轴承故障特征频率
        var bearingFreqs = CalculateBearingFaultFrequencies(
            rotationalFreq,
            motorParams.BearingRollingElements,
            motorParams.BearingBallDiameter,
            motorParams.BearingPitchDiameter,
            motorParams.BearingContactAngle);

        // 提取关键频率幅值
        var result = new FftAnalysisResult
        {
            SampleCount = samples.Length,
            SampleRate = sampleRate,
            FrequencyResolution = freqResolution,
            FundamentalFrequency = fundamentalFreq,
            RotationalFrequency = rotationalFreq,

            // 基频及谐波幅值
            FundamentalAmplitude = GetAmplitudeAtFrequency(magnitudes, fundamentalFreq, freqResolution),
            SecondHarmonicAmplitude = GetAmplitudeAtFrequency(magnitudes, fundamentalFreq * 2, freqResolution),
            ThirdHarmonicAmplitude = GetAmplitudeAtFrequency(magnitudes, fundamentalFreq * 3, freqResolution),

            // 轴承故障特征频率
            BearingFaultFrequencies = bearingFreqs,
            BpfoAmplitude = GetAmplitudeAtFrequency(magnitudes, bearingFreqs.Bpfo, freqResolution),
            BpfiAmplitude = GetAmplitudeAtFrequency(magnitudes, bearingFreqs.Bpfi, freqResolution),
            BsfAmplitude = GetAmplitudeAtFrequency(magnitudes, bearingFreqs.Bsf, freqResolution),
            FtfAmplitude = GetAmplitudeAtFrequency(magnitudes, bearingFreqs.Ftf, freqResolution),

            // 边带频率 (f ± fr) 用于检测转子故障
            UpperSidebandAmplitude = GetAmplitudeAtFrequency(magnitudes, fundamentalFreq + rotationalFreq, freqResolution),
            LowerSidebandAmplitude = GetAmplitudeAtFrequency(magnitudes, fundamentalFreq - rotationalFreq, freqResolution),

            // 频谱统计特征
            TotalRmsAmplitude = CalculateRms(magnitudes),
            PeakAmplitude = magnitudes.Max(),
            PeakFrequency = Array.IndexOf(magnitudes, magnitudes.Max()) * freqResolution,

            // 频带能量分布
            LowFreqEnergy = CalculateBandEnergy(magnitudes, freqResolution, 0, 100),
            MidFreqEnergy = CalculateBandEnergy(magnitudes, freqResolution, 100, 1000),
            HighFreqEnergy = CalculateBandEnergy(magnitudes, freqResolution, 1000, sampleRate / 2),

            // 原始频谱数据（可选存储）
            FrequencyBins = Enumerable.Range(0, Math.Min(magnitudes.Length, 1000))
                .Select(i => i * freqResolution).ToArray(),
            MagnitudeBins = magnitudes.Take(1000).ToArray()
        };

        return result;
    }

    /// <summary>
    /// 计算轴承故障特征频率
    /// BPFO: 外圈故障频率, BPFI: 内圈故障频率, BSF: 滚动体故障频率, FTF: 保持架故障频率
    /// </summary>
    public BearingFaultFrequencies CalculateBearingFaultFrequencies(
        double rotationalFreq, int? rollingElements, double? ballDiameter,
        double? pitchDiameter, double? contactAngle)
    {
        // 如果缺少轴承参数，使用默认估算值
        var n = rollingElements ?? 8;  // 滚动体数量
        var bd = ballDiameter ?? 10.0; // 滚动体直径 mm
        var pd = pitchDiameter ?? 50.0; // 节圆直径 mm
        var theta = (contactAngle ?? 0) * Math.PI / 180; // 接触角转弧度

        var cosTheta = Math.Cos(theta);
        var ratio = bd / pd;

        // 标准轴承故障频率公式
        var bpfo = (n / 2.0) * rotationalFreq * (1 - ratio * cosTheta);
        var bpfi = (n / 2.0) * rotationalFreq * (1 + ratio * cosTheta);
        var bsf = (pd / (2 * bd)) * rotationalFreq * (1 - Math.Pow(ratio * cosTheta, 2));
        var ftf = (rotationalFreq / 2.0) * (1 - ratio * cosTheta);

        return new BearingFaultFrequencies
        {
            Bpfo = bpfo,
            Bpfi = bpfi,
            Bsf = bsf,
            Ftf = ftf,
            RotationalFrequency = rotationalFreq
        };
    }

    /// <summary>
    /// 从频谱创建 FrequencyProfile 用于基线存储
    /// </summary>
    public FrequencyProfile CreateFrequencyProfile(FftAnalysisResult result)
    {
        // 计算总谐波畸变 (THD%)
        var thd = result.FundamentalAmplitude > 0
            ? Math.Sqrt(
                Math.Pow(result.SecondHarmonicAmplitude, 2) +
                Math.Pow(result.ThirdHarmonicAmplitude, 2)) / result.FundamentalAmplitude * 100
            : 0;

        return new FrequencyProfile
        {
            FundamentalFreq = result.FundamentalFrequency,
            FundamentalAmplitude = result.FundamentalAmplitude,
            HarmonicAmplitudes = new[]
            {
                result.SecondHarmonicAmplitude,
                result.ThirdHarmonicAmplitude
            },
            TotalHarmonicDistortion = thd,
            BearingAmplitudes = new BearingFaultAmplitudes
            {
                BPFO = result.BpfoAmplitude,
                BPFI = result.BpfiAmplitude,
                BSF = result.BsfAmplitude,
                FTF = result.FtfAmplitude
            },
            NoiseFloor = 0, // 需要更复杂的算法计算噪声底
            SpectralEnergy = result.LowFreqEnergy + result.MidFreqEnergy + result.HighFreqEnergy
        };
    }

    #region FFT Implementation (Cooley-Tukey)

    private static void Fft(Complex[] data)
    {
        int n = data.Length;
        if (n <= 1) return;

        // Bit-reversal permutation
        int bits = (int)Math.Log2(n);
        for (int i = 0; i < n; i++)
        {
            int j = BitReverse(i, bits);
            if (j > i)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        // Cooley-Tukey iterative FFT
        for (int len = 2; len <= n; len *= 2)
        {
            double angle = -2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + len / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }
    }

    private static int BitReverse(int x, int bits)
    {
        int result = 0;
        for (int i = 0; i < bits; i++)
        {
            result = (result << 1) | (x & 1);
            x >>= 1;
        }
        return result;
    }

    private static void ApplyHanningWindow(Complex[] data, int originalLength)
    {
        for (int i = 0; i < originalLength; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (originalLength - 1)));
            data[i] = new Complex(data[i].Real * window, 0);
        }
    }

    private static int NextPowerOfTwo(int n)
    {
        int power = 1;
        while (power < n) power *= 2;
        return power;
    }

    #endregion

    #region Helper Methods

    private static double GetAmplitudeAtFrequency(double[] magnitudes, double targetFreq, double freqResolution)
    {
        if (targetFreq <= 0 || freqResolution <= 0) return 0;

        int index = (int)Math.Round(targetFreq / freqResolution);
        if (index < 0 || index >= magnitudes.Length) return 0;

        // 取目标频率及其邻近频率的最大值（考虑频谱泄漏）
        int start = Math.Max(0, index - 2);
        int end = Math.Min(magnitudes.Length - 1, index + 2);

        double maxAmplitude = 0;
        for (int i = start; i <= end; i++)
            maxAmplitude = Math.Max(maxAmplitude, magnitudes[i]);

        return maxAmplitude;
    }

    private static double CalculateRms(double[] magnitudes)
    {
        if (magnitudes.Length == 0) return 0;
        double sumSquares = magnitudes.Sum(m => m * m);
        return Math.Sqrt(sumSquares / magnitudes.Length);
    }

    private static double CalculateBandEnergy(double[] magnitudes, double freqResolution, double lowFreq, double highFreq)
    {
        int startIndex = (int)(lowFreq / freqResolution);
        int endIndex = (int)(highFreq / freqResolution);

        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(magnitudes.Length - 1, endIndex);

        double energy = 0;
        for (int i = startIndex; i <= endIndex; i++)
            energy += magnitudes[i] * magnitudes[i];

        return energy;
    }

    #endregion
}

/// <summary>
/// FFT 分析所需的电机参数
/// </summary>
public sealed class MotorFftParams
{
    /// <summary>电源频率 (Hz)，通常 50 或 60</summary>
    public double SupplyFrequency { get; init; } = 50;

    /// <summary>转速 (RPM)</summary>
    public double RotationalSpeed { get; init; }

    /// <summary>轴承滚动体数量</summary>
    public int? BearingRollingElements { get; init; }

    /// <summary>轴承滚动体直径 (mm)</summary>
    public double? BearingBallDiameter { get; init; }

    /// <summary>轴承节圆直径 (mm)</summary>
    public double? BearingPitchDiameter { get; init; }

    /// <summary>轴承接触角 (度)</summary>
    public double? BearingContactAngle { get; init; }

    /// <summary>
    /// 从 MotorModel 创建 FFT 参数
    /// </summary>
    public static MotorFftParams FromModel(MotorModel model, double currentSpeed)
    {
        return new MotorFftParams
        {
            SupplyFrequency = model.RatedFrequency ?? 50,
            RotationalSpeed = currentSpeed > 0 ? currentSpeed : model.RatedSpeed ?? 1500,
            BearingRollingElements = model.BearingRollingElements,
            BearingBallDiameter = model.BearingBallDiameter,
            BearingPitchDiameter = model.BearingPitchDiameter,
            BearingContactAngle = model.BearingContactAngle
        };
    }
}

/// <summary>
/// 轴承故障特征频率
/// </summary>
public sealed class BearingFaultFrequencies
{
    /// <summary>外圈故障频率 (Ball Pass Frequency Outer)</summary>
    public double Bpfo { get; init; }

    /// <summary>内圈故障频率 (Ball Pass Frequency Inner)</summary>
    public double Bpfi { get; init; }

    /// <summary>滚动体故障频率 (Ball Spin Frequency)</summary>
    public double Bsf { get; init; }

    /// <summary>保持架故障频率 (Fundamental Train Frequency)</summary>
    public double Ftf { get; init; }

    /// <summary>转频 (Hz)</summary>
    public double RotationalFrequency { get; init; }
}

/// <summary>
/// FFT 分析结果
/// </summary>
public sealed class FftAnalysisResult
{
    public static FftAnalysisResult Empty => new();

    // 基本参数
    public int SampleCount { get; init; }
    public double SampleRate { get; init; }
    public double FrequencyResolution { get; init; }
    public double FundamentalFrequency { get; init; }
    public double RotationalFrequency { get; init; }

    // 基频及谐波
    public double FundamentalAmplitude { get; init; }
    public double SecondHarmonicAmplitude { get; init; }
    public double ThirdHarmonicAmplitude { get; init; }

    // 轴承故障特征频率
    public BearingFaultFrequencies? BearingFaultFrequencies { get; init; }
    public double BpfoAmplitude { get; init; }
    public double BpfiAmplitude { get; init; }
    public double BsfAmplitude { get; init; }
    public double FtfAmplitude { get; init; }

    // 边带频率
    public double UpperSidebandAmplitude { get; init; }
    public double LowerSidebandAmplitude { get; init; }

    // 频谱统计
    public double TotalRmsAmplitude { get; init; }
    public double PeakAmplitude { get; init; }
    public double PeakFrequency { get; init; }

    // 频带能量
    public double LowFreqEnergy { get; init; }
    public double MidFreqEnergy { get; init; }
    public double HighFreqEnergy { get; init; }

    // 原始频谱（截取前 1000 个频率点）
    public double[] FrequencyBins { get; init; } = Array.Empty<double>();
    public double[] MagnitudeBins { get; init; } = Array.Empty<double>();
}
