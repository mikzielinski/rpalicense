using System;
using Ops.Runtime.Seed;
using UiPath.CodedWorkflows;
using UiPath.Testing;

namespace RuntimeGateTest;

/// <summary>
/// Manual entry point for attended runs on MacBook Pro / Orchestrator jobs.
/// Set input Scenario to one of: valid, wrong-token, disabled, expired, wrong-machine.
/// </summary>
public class Main : CodedWorkflow
{
    [Workflow]
    public void Execute(string Scenario = "valid", string RuntimeToken = "", string MachineAlias = "")
    {
        var token = string.IsNullOrWhiteSpace(RuntimeToken)
            ? Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
              ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN")
              ?? Environment.GetEnvironmentVariable("FLOW_RUNTIME_TEST_TOKEN")
              ?? "RT-2026-CLIENT-001"
            : RuntimeToken;

        var machine = string.IsNullOrWhiteSpace(MachineAlias)
            ? Environment.GetEnvironmentVariable("FLOW_RUNTIME_TEST_MACHINE") ?? Environment.MachineName
            : MachineAlias;

        Log($"RuntimeGateTest starting. scenario={Scenario}, machine={machine}, token={Mask(token)}");

        switch (Scenario.Trim().ToLowerInvariant())
        {
            case "valid":
                RunExpectSuccess(token, machine, "boot-ok-remote");
                break;
            case "wrong-token":
                RunExpectFailure("RT-DOES-NOT-EXIST", machine, "boot-0x11");
                break;
            case "disabled":
                RunExpectFailure(token, machine, "boot-0x12");
                break;
            case "expired":
                RunExpectFailure(token, machine, "boot-0x14");
                break;
            case "wrong-machine":
                RunExpectFailure(token, "UNKNOWN-MACHINE", "boot-0x15");
                break;
            case "empty-token":
                RunExpectFailure("", machine, "boot-0x01");
                break;
            default:
                throw new InvalidOperationException($"Unknown scenario: {Scenario}");
        }

        Log("RuntimeGateTest finished successfully.");
    }

    private void RunExpectSuccess(string token, string machine, string expectedCode)
    {
        var profile = Bootstrapper.Initialize(token, machine);
        var check = Bootstrapper.LastCheck;
        AssertEqual(expectedCode, check.Code, "Validation code");
        AssertTrue(check.Success, "Validation success flag");
        AssertFalse(check.UsedCache, "Expected remote validation, not cache");
        AssertFalse(string.IsNullOrWhiteSpace(profile.ApiEndpoint), "ApiEndpoint");
        Log($"OK profile.ApiEndpoint={profile.ApiEndpoint}");
    }

    private void RunExpectFailure(string token, string machine, string expectedCode)
    {
        try
        {
            _ = Bootstrapper.Initialize(token, machine);
            throw new InvalidOperationException("Initialize should have failed.");
        }
        catch (Exception ex)
        {
            var check = Bootstrapper.LastCheck;
            var code = !string.IsNullOrWhiteSpace(check.Code) ? check.Code : ex.Message;
            AssertEqual(expectedCode, code, "Validation code");
            AssertFalse(check.Success, "Validation success flag");
            Log($"OK expected failure code={code}");
        }
    }

    private static string Mask(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 8)
        {
            return "***";
        }

        return token[..4] + "..." + token[^4..];
    }

    private void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void AssertFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
        }
    }
}
