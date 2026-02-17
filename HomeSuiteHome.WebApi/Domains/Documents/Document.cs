namespace HomeSuiteHome.WebApi.Domains.Documents;

public class Document
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}