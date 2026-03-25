namespace Homestead.WebApi.Domain.Auditing;

public interface IAuditSnapshot<TSelf, TSource> where TSelf : IAuditSnapshot<TSelf, TSource>
{
    static abstract TSelf From(TSource source);
    List<FieldChange> Diff(TSelf other);
}
