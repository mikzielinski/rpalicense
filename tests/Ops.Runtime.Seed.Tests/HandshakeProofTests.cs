using UiPath.System.RoboticSecurity;
using Xunit;

namespace Ops.Runtime.Seed.Tests;

public sealed class HandshakeProofTests
{
  [Fact]
  public void RuntimeProof_roundtrip_matches()
  {
    const string token = "test-runtime-token-001";
    const string pepper = "test-pepper-ops-runtime-seed-2026";
    const string challengeId = "abc123";
    const string serverNonce = "server-nonce-b64";
    const string clientNonce = "client-nonce-b64";
    const string machine = "ROBOT-01";

    var proof = HandshakeProof.ComputeRuntimeProof(
      token, pepper, challengeId, serverNonce, clientNonce, machine, token);

    Assert.True(HandshakeProof.VerifyRuntimeProof(
      token, pepper, challengeId, serverNonce, clientNonce, machine, token, proof));
  }

  [Fact]
  public void OperatorProof_roundtrip_matches()
  {
    const string secret = "operator-secret-value";
    const string operatorId = "mikolaj";
    const string challengeId = "def456";
    const string serverNonce = "srv";
    const string clientNonce = "cli";

    var proof = HandshakeProof.ComputeOperatorProof(
      secret, challengeId, serverNonce, clientNonce, operatorId);

    Assert.True(HandshakeProof.VerifyOperatorProof(
      secret, challengeId, serverNonce, clientNonce, operatorId, proof));
  }

  [Fact]
  public void RuntimeProof_rejects_wrong_token()
  {
    var proof = HandshakeProof.ComputeRuntimeProof(
      "token-a", "pepper", "c", "s", "cl", "MACHINE", "token-a");

    Assert.False(HandshakeProof.VerifyRuntimeProof(
      "token-b", "pepper", "c", "s", "cl", "MACHINE", "token-b", proof));
  }
}
