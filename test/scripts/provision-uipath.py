#!/usr/bin/env python3
"""Provision UiPath Orchestrator resources for Ops.Runtime.Seed testing.

Uses OAuth client credentials (External Application) against Automation Cloud / staging.

Required environment variables:
  UIPATH_CLIENT_ID
  UIPATH_CLIENT_SECRET
  UIPATH_SCOPES            (e.g. OR.Folders OR.Assets OR.Machines OR.Robots OR.Settings OR.Jobs)

Optional:
  UIPATH_BASE_URL          default: https://staging.uipath.com/mzpocevylrxu
  UIPATH_IDENTITY_URL      default: https://staging.uipath.com/identity_/connect/token
  UIPATH_FOLDER_NAME       default: RuntimeGate
  UIPATH_MACHINE_NAME      default: MacBookPro-Robot
  UIPATH_ASSET_NAME        default: RuntimeToken
  UIPATH_RUNTIME_TOKEN     default: RT-2026-CLIENT-001
  UIPATH_MACHINE_HOST      default: ROBOT01 (must match catalog hosts + FLOW_RUNTIME_TEST_MACHINE)
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


def env(name: str, default: str | None = None) -> str:
    value = os.environ.get(name, default)
    if value is None or value.strip() == "":
        raise SystemExit(f"Missing required environment variable: {name}")
    return value.strip()


def http_json(method: str, url: str, token: str | None = None, body: dict | None = None, headers: dict | None = None) -> Any:
    data = None
    req_headers = {"Accept": "application/json"}
    if headers:
        req_headers.update(headers)
    if token:
        req_headers["Authorization"] = f"Bearer {token}"
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        req_headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, method=method, headers=req_headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as ex:
        detail = ex.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {url} -> HTTP {ex.code}: {detail}") from ex


def fetch_token(identity_url: str, client_id: str, client_secret: str, scopes: str) -> str:
    form = urllib.parse.urlencode(
        {
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
            "scope": scopes,
        }
    ).encode("utf-8")
    req = urllib.request.Request(
        identity_url,
        data=form,
        method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as ex:
        detail = ex.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Token request failed HTTP {ex.code}: {detail}") from ex
    token = payload.get("access_token")
    if not token:
        raise RuntimeError(f"No access_token in response: {payload}")
    return token


def orch(base: str, path: str) -> str:
    base = base.rstrip("/")
    if not path.startswith("/"):
        path = "/" + path
    if "/orchestrator_" in base:
        return f"{base}{path}"
    return f"{base}/orchestrator_{path}"


def ensure_folder(token: str, base: str, folder_name: str) -> dict:
    folders = http_json("GET", orch(base, f"/odata/Folders?$filter=DisplayName eq '{folder_name}'"), token)
    values = folders.get("value", [])
    if values:
        print(f"Folder exists: {folder_name} (Id={values[0]['Id']})")
        return values[0]
    created = http_json(
        "POST",
        orch(base, "/odata/Folders"),
        token,
        body={
            "DisplayName": folder_name,
            "ProvisionType": "Manual",
            "PermissionModel": "FineGrained",
        },
    )
    print(f"Created folder: {folder_name} (Id={created['Id']})")
    return created


def ensure_asset(token: str, base: str, folder_id: int, asset_name: str, token_value: str) -> dict:
    headers = {"X-UIPATH-OrganizationUnitId": str(folder_id)}
    existing = http_json(
        "GET",
        orch(base, f"/odata/Assets?$filter=Name eq '{asset_name}'"),
        token,
        headers=headers,
    )
    values = existing.get("value", [])
    body = {
        "Name": asset_name,
        "ValueScope": "Global",
        "ValueType": "Text",
        "StringValue": token_value,
    }
    if values:
        asset_id = values[0]["Id"]
        http_json("PUT", orch(base, f"/odata/Assets({asset_id})"), token, body=body, headers=headers)
        print(f"Updated asset: {asset_name} = {token_value}")
        return values[0]
    created = http_json("POST", orch(base, "/odata/Assets"), token, body=body, headers=headers)
    print(f"Created asset: {asset_name} = {token_value}")
    return created


def ensure_machine(token: str, base: str, folder_id: int, machine_name: str) -> dict:
    headers = {"X-UIPATH-OrganizationUnitId": str(folder_id)}
    existing = http_json(
        "GET",
        orch(base, f"/odata/Machines?$filter=Name eq '{machine_name}'"),
        token,
        headers=headers,
    )
    values = existing.get("value", [])
    body = {
        "Name": machine_name,
        "Type": "Standard",
        "Scope": "Default",
        "NonProductionSlots": 1,
        "UnattendedSlots": 0,
        "TestAutomationSlots": 0,
        "HeadlessSlots": 0,
        "AutomationCloudSlots": 0,
        "AutomationCloudTestAutomationSlots": 0,
        "Key": machine_name,
    }
    if values:
        print(f"Machine exists: {machine_name} (Id={values[0]['Id']})")
        return values[0]
    created = http_json("POST", orch(base, "/odata/Machines"), token, body=body, headers=headers)
    print(f"Created machine: {machine_name} (Id={created['Id']})")
    return created


def probe_orchestrator(base: str) -> None:
    url = orch(base, "/odata/Folders")
    try:
        urllib.request.urlopen(url, timeout=15)
    except urllib.error.HTTPError as ex:
        if ex.code in (401, 403):
            print("Orchestrator endpoint reachable (auth required).")
            return
        body = ex.read().decode("utf-8", errors="replace")
        if "not enabled" in body.lower():
            raise SystemExit(
                "Orchestrator is not enabled for this tenant. Enable Automation Cloud Orchestrator in admin portal first."
            ) from ex
        raise


def main() -> int:
    base = env("UIPATH_BASE_URL", "https://staging.uipath.com/mzpocevylrxu")
    identity = env("UIPATH_IDENTITY_URL", "https://staging.uipath.com/identity_/connect/token")
    client_id = env("UIPATH_CLIENT_ID")
    client_secret = env("UIPATH_CLIENT_SECRET")
    scopes = env("UIPATH_SCOPES", "OR.Folders OR.Assets OR.Machines OR.Settings OR.Jobs")
    folder_name = env("UIPATH_FOLDER_NAME", "RuntimeGate")
    machine_name = env("UIPATH_MACHINE_NAME", "MacBookPro-Robot")
    asset_name = env("UIPATH_ASSET_NAME", "RuntimeToken")
    runtime_token = env("UIPATH_RUNTIME_TOKEN", "RT-2026-CLIENT-001")

    print(f"Base URL: {base}")
    probe_orchestrator(base)

    print("Requesting OAuth token...")
    token = fetch_token(identity, client_id, client_secret, scopes)

    folder = ensure_folder(token, base, folder_name)
    folder_id = folder["Id"]
    ensure_machine(token, base, folder_id, machine_name)
    ensure_asset(token, base, folder_id, asset_name, runtime_token)

    print()
    print("Provisioning complete.")
    print(json.dumps(
        {
            "folder": folder_name,
            "folderId": folder_id,
            "machine": machine_name,
            "asset": asset_name,
            "runtimeToken": runtime_token,
            "orchestratorUrl": base,
        },
        indent=2,
    ))
    print()
    print("Next on MacBook Pro:")
    print("  1. uip login --tenant <your-tenant>")
    print("  2. source test/fixtures/runtime.env")
    print("  3. test/scripts/serve-seed-jwt.sh")
    print("  4. cd test/uipath/RuntimeGateTest && uip rpa build")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        raise SystemExit(2) from ex
