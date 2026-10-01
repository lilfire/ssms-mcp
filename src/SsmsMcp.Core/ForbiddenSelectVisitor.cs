using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsMcp.Core;

public sealed class ForbiddenSelectVisitor : TSqlFragmentVisitor
{
    public bool HasForbiddenConstruct { get; private set; }

    public override void ExplicitVisit(SelectStatement node)
    {
        if (node.Into is not null)
            HasForbiddenConstruct = true;

        base.ExplicitVisit(node);
    }

    public override void Visit(TSqlFragment node)
    {
        if (node.GetType().Name == "NextValueForExpression")
            HasForbiddenConstruct = true;

        base.Visit(node);
    }
}
