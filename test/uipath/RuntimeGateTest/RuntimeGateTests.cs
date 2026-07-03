using System;
using Ops.Runtime.Seed;
using UiPath.CodedWorkflows;
using UiPath.Testing;

namespace RuntimeGateTest;

/// <summary>
/// Coded test cases mirroring test/Ops.Runtime.Seed.TestHarness scenarios.
/// Run with: uip rpa test run --project-dir test/uipath/RuntimeGateTest
/// </summary>
public class RuntimeGateTests : CodedWorkflow
{
    private string Token => Environment.GetEnvironmentVariable("FLOW_RUNTIME_TEST_TOKEN") ?? "RT-2026-CLIENT-001";
    private string Machine => Environment.GetEnvironmentVariable("FLOW_RUNTIME_TEST_MACHINE") ?? "ROBOT01";

    [TestCase]
    public void ValidLicense_Remote()
    {
        var profile = Bootstrapper.Initialize(Token, Machine);
        testing.VerifyExpression(Bootstrapper.LastCheck.Success, "Success should be true");
        testing.VerifyExpression(!Bootstrapper.LastCheck.UsedCache, "Should not use cache");
        testing.VerifyAreEqual("boot-ok-remote", Bootstrapper.LastCheck.Code, "Validation code");
        testing.VerifyExpression(!string.IsNullOrWhiteSpace(profile.ApiEndpoint), "ApiEndpoint present");
    }

    [TestCase]
    public void WrongToken_Boot0x11()
    {
        ExpectFailure("RT-DOES-NOT-EXIST", Machine, "boot-0x11");
    }

    [TestCase]
    public void EmptyToken_Boot0x01()
    {
        ExpectFailure("", Machine, "boot-0x01");
    }

    [TestCase]
    public void WrongMachine_Boot0x15()
    {
        ExpectFailure(Token, "UNKNOWN-MACHINE", "boot-0x15");
    }

    [TestCase]
    public void TryInitialize_Failure()
    {
        var ok = Bootstrapper.TryInitialize("RT-DOES-NOT-EXIST", out _);
        testing.VerifyExpression(!ok, "TryInitialize should return false");
        testing.VerifyAreEqual("boot-0x11", Bootstrapper.LastCheck.Code, "Validation code");
    }

    private void ExpectFailure(string token, string machine, string expectedCode)
    {
        try
        {
            _ = Bootstrapper.Initialize(token, machine);
            throw new InvalidOperationException("Expected Initialize to fail.");
        }
        catch (Exception)
        {
            testing.VerifyAreEqual(expectedCode, Bootstrapper.LastCheck.Code, "Validation code");
            testing.VerifyExpression(!Bootstrapper.LastCheck.Success, "Success should be false");
        }
    }
}
