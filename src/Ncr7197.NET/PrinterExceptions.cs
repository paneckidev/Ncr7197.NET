namespace Ncr7197;

public class Ncr7197Exception : Exception
{
    public Ncr7197Exception(string message) : base(message) { }
    public Ncr7197Exception(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class Ncr7197ValidationException(string message) : Ncr7197Exception(message);
public sealed class Ncr7197CommunicationException(string message, Exception innerException) : Ncr7197Exception(message, innerException);
