using MSAVA_INF.Models;

namespace MSAVA_INF.Environment;

public interface ILocalEnvironment
{
    LocalEnvironmentValues Values { get; }

    byte[] GetSigningKeyBytes();
}
