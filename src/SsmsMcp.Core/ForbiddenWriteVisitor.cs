using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsMcp.Core;

public sealed class ForbiddenWriteVisitor : TSqlFragmentVisitor
{
    private readonly TSqlStatement _rootStatement;

    public ForbiddenWriteVisitor(TSqlStatement rootStatement)
    {
        _rootStatement = rootStatement;
    }

    public bool HasForbiddenConstruct { get; private set; }

    public override void Visit(TSqlFragment node)
    {
        bool nestedWrite = node is TSqlStatement statement && !ReferenceEquals(statement, _rootStatement) &&
            (statement is DeleteStatement || statement is UpdateStatement || statement is InsertStatement);
        if (node is SelectStatement select && select.Into is not null || nestedWrite ||
            node.GetType().Name is "NextValueForExpression" or "OutputIntoClause" or "ExecuteInsertSource")
            HasForbiddenConstruct = true;

        base.Visit(node);
    }
}
