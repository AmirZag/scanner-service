namespace EsclReferenceClient.Escl;

public sealed class EsclProtocolException : Exception
{
    public EsclProtocolException(string message) : base(message) { }
}

public sealed class EsclDocumentNotFoundException : Exception
{
    public EsclDocumentNotFoundException()
        : base("The scanner has no further documents to transfer (NextDocument returned 404).") { }
}
