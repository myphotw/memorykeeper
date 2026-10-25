using System.Net;

namespace MemoryKeeper.Mobile.Http;

public sealed class MobileBackendConfigurationException : InvalidOperationException
{
    public MobileBackendConfigurationException()
        : base("Mobile Backend configuration is unavailable.")
    {
    }
}

public sealed class MobileBackendApiException : HttpRequestException
{
    public MobileBackendApiException(HttpStatusCode statusCode)
        : base("Mobile Backend request failed.", inner: null, statusCode: statusCode)
    {
    }
}

public sealed class MobileBackendContractException : InvalidOperationException
{
    public MobileBackendContractException(Exception innerException)
        : base("Mobile Backend returned an invalid response.", innerException)
    {
    }
}
