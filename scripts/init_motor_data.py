#!/usr/bin/env python3
"""Motor fault prediction system - Demo data initialization script (stdlib only)"""

import json
import urllib.request
import urllib.error
import time

API_BASE = "http://localhost:5000/api"

def api_request(method, endpoint, data=None, token=None):
    """Make API request using urllib"""
    url = f"{API_BASE}{endpoint}"
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"

    body = json.dumps(data).encode('utf-8') if data else None
    req = urllib.request.Request(url, data=body, headers=headers, method=method)

    try:
        with urllib.request.urlopen(req) as resp:
            resp_data = json.loads(resp.read().decode('utf-8'))
            # Handle paginated response: {items: [...], totalCount: n, pageNumber: n, pageSize: n}
            if isinstance(resp_data, dict) and 'items' in resp_data:
                return resp.status, resp_data['items']
            return resp.status, resp_data
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8')

def main():
    # 1. Login
    print("=== 1. Login ===")
    status, resp = api_request("POST", "/auth/login", {"username": "admin", "password": "admin123"})
    token = resp["data"]["token"]
    print(f"Token obtained: {token[:50]}...")

    # 2. Create Motor Models
    print("\n=== 2. Create Motor Models ===")
    motor_models = [
        {
            "name": "Standard Induction Motor 15kW",
            "description": "Three-phase induction motor for industrial drive",
            "type": 0,
            "ratedPower": 15.0,
            "ratedVoltage": 380,
            "ratedCurrent": 30,
            "ratedSpeed": 1480,
            "ratedFrequency": 50,
            "polePairs": 2,
            "vfdModel": "ABB ACS580",
            "bearingModel": "SKF 6308",
            "bearingRollingElements": 8,
            "bearingBallDiameter": 15.875,
            "bearingPitchDiameter": 58.5,
            "bearingContactAngle": 0
        },
        {
            "name": "VFD Motor 7.5kW",
            "description": "Variable frequency drive motor",
            "type": 0,
            "ratedPower": 7.5,
            "ratedVoltage": 380,
            "ratedCurrent": 15,
            "ratedSpeed": 1450,
            "ratedFrequency": 50,
            "polePairs": 2,
            "vfdModel": "Siemens G120",
            "bearingModel": "SKF 6206"
        }
    ]

    created_models = []
    for model in motor_models:
        status, resp = api_request("POST", "/motor-models", model, token)
        if status == 201:
            created_models.append(resp)
            print(f"  Created: {resp['name']} [{resp['modelId']}]")
        else:
            print(f"  Error creating {model['name']}: {resp}")

    if not created_models:
        print("No models created, checking existing...")
        status, resp = api_request("GET", "/motor-models", token=token)
        created_models = resp
        print(f"  Found {len(created_models)} existing models")

    # 3. Get existing devices
    print("\n=== 3. Get Devices ===")
    status, devices = api_request("GET", "/devices", token=token)
    # Debug: print raw response
    if isinstance(devices, str):
        print(f"  Raw response: {devices[:200]}")
        devices = []
    elif isinstance(devices, dict):
        print(f"  Response keys: {devices.keys()}")
        # Try to extract items from various response formats
        if 'data' in devices:
            devices = devices['data'] if isinstance(devices['data'], list) else []
    print(f"  Found {len(devices)} devices")
    for d in devices:
        if isinstance(d, dict):
            print(f"    - {d.get('deviceId', 'N/A')}: {d.get('name', 'N/A')}")

    # 4. Create Motor Instances
    print("\n=== 4. Create Motor Instances ===")
    motor_instances = [
        {
            "modelId": created_models[0]["modelId"],
            "deviceId": "Motor-001",
            "name": "Main Drive Motor #1",
            "location": "Workshop A - Line 1",
            "installDate": "2024-01-15",
            "assetNumber": "MTR-2024-001"
        },
        {
            "modelId": created_models[1]["modelId"] if len(created_models) > 1 else created_models[0]["modelId"],
            "deviceId": "SIM-PLC-001",
            "name": "Auxiliary Motor #2",
            "location": "Workshop A - Line 2",
            "installDate": "2024-03-20",
            "assetNumber": "MTR-2024-002"
        }
    ]

    created_instances = []
    for inst in motor_instances:
        status, resp = api_request("POST", "/motor-instances", inst, token)
        if status == 201:
            created_instances.append(resp)
            print(f"  Created: {resp['name']} [{resp['instanceId']}]")
        else:
            print(f"  Error creating {inst['name']}: {resp}")

    if not created_instances:
        print("No instances created, checking existing...")
        status, resp = api_request("GET", "/motor-instances", token=token)
        created_instances = resp
        print(f"  Found {len(created_instances)} existing instances")

    # 5. Create Parameter Mappings
    print("\n=== 5. Create Parameter Mappings ===")
    parameter_mappings = {
        "Motor-001": [
            {"parameter": 40, "tagId": "Motor1_Temp", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 3, "tagId": "Motor1_Current", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 21, "tagId": "Motor1_Speed", "scaleFactor": 30, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 20, "tagId": "Torque", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": True}
        ],
        "SIM-PLC-001": [
            {"parameter": 40, "tagId": "Motor1_Temp", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 3, "tagId": "Motor1_Current", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 21, "tagId": "Motor1_Speed", "scaleFactor": 30, "offset": 0, "usedForDiagnosis": True},
            {"parameter": 34, "tagId": "Motor1_Running", "scaleFactor": 1.0, "offset": 0, "usedForDiagnosis": False}
        ]
    }

    for inst in created_instances:
        device_id = inst["deviceId"]
        if device_id in parameter_mappings:
            mappings = parameter_mappings[device_id]
            status, resp = api_request(
                "POST",
                f"/motor-instances/{inst['instanceId']}/mappings/batch",
                mappings,
                token
            )
            if status == 200:
                print(f"  Created {resp['created']} mappings for {inst['name']}")
            else:
                print(f"  Error: {resp}")

    # 6. Create Operation Modes
    print("\n=== 6. Create Operation Modes ===")
    operation_modes = [
        {
            "name": "Normal Operation",
            "description": "Motor running at normal speed",
            "triggerTagId": "Motor1_Speed",
            "triggerMinValue": 30,
            "triggerMaxValue": 60,
            "minDurationMs": 5000,
            "maxDurationMs": 0,
            "priority": 1
        },
        {
            "name": "Low Speed",
            "description": "Motor running at low speed",
            "triggerTagId": "Motor1_Speed",
            "triggerMinValue": 10,
            "triggerMaxValue": 30,
            "minDurationMs": 3000,
            "maxDurationMs": 0,
            "priority": 2
        },
        {
            "name": "Idle/Standby",
            "description": "Motor idle or standby",
            "triggerTagId": "Motor1_Speed",
            "triggerMinValue": 0,
            "triggerMaxValue": 10,
            "minDurationMs": 2000,
            "maxDurationMs": 0,
            "priority": 3
        }
    ]

    for inst in created_instances:
        for mode in operation_modes:
            status, resp = api_request(
                "POST",
                f"/motor-instances/{inst['instanceId']}/modes",
                mode,
                token
            )
            if status == 201:
                print(f"  Created mode '{mode['name']}' for {inst['name']}")
            else:
                print(f"  Error: {resp}")

    # 7. Start Baseline Learning
    print("\n=== 7. Start Baseline Learning ===")
    end_ts = int(time.time() * 1000)
    start_ts = end_ts - (60 * 60 * 1000)  # 1 hour ago

    for inst in created_instances:
        status, resp = api_request(
            "POST",
            f"/motor-instances/{inst['instanceId']}/learn-all",
            {"startTs": start_ts, "endTs": end_ts},
            token
        )
        if status == 200:
            print(f"  Started learning for {inst['name']}: {resp}")
        else:
            print(f"  Error: {resp}")

    # 8. Wait and verify
    print("\n=== 8. Verifying Results ===")
    time.sleep(3)

    status, instances = api_request("GET", "/motor-instances", token=token)
    print(f"  Total motor instances: {len(instances)}")

    for inst in instances:
        status, detail = api_request(
            "GET",
            f"/motor-instances/{inst['instanceId']}/detail",
            token=token
        )
        print(f"\n  {inst['name']}:")
        print(f"    Model: {detail.get('model', {}).get('name', 'N/A') if detail.get('model') else 'N/A'}")
        print(f"    Mappings: {len(detail.get('mappings', []))}")
        print(f"    Modes: {len(detail.get('modes', []))}")
        print(f"    Baselines: {detail.get('baselineCount', 0)}")

    # 9. Test diagnosis
    print("\n=== 9. Test Diagnosis ===")
    for inst in instances:
        status, result = api_request(
            "POST",
            f"/motor-instances/{inst['instanceId']}/diagnose",
            {},
            token
        )
        if status == 200:
            print(f"  {inst['name']}: Health Score = {result.get('healthScore', 'N/A')}")
        else:
            print(f"  {inst['name']}: {result}")

    print("\n=== Initialization Complete ===")
    print("Please refresh the Motor Prediction page")

if __name__ == "__main__":
    main()
