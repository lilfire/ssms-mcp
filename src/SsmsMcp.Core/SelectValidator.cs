using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsMcp.Core;

public sealed class SelectValidator
{
    public void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 65536)
            throw new ArgumentException("SQL-teksten må inneholde én kort SELECT-setning.", nameof(sql));

        TSql160Parser parser = new(true);
        TSqlFragment fragment = parser.Parse(new StringReader(sql), out IList<ParseError> errors);
        if (errors.Count > 0 || fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count != 1 || script.Batches[0].Statements[0] is not SelectStatement)
            throw new InvalidOperationException("Kun én gyldig SELECT-setning er tillatt.");

        ForbiddenSelectVisitor visitor = new();
        fragment.Accept(visitor);
        if (visitor.HasForbiddenConstruct)
            throw new InvalidOperationException("SELECT INTO og sekvensuttrykk er ikke tillatt.");
    }
}
