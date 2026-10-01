using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsMcp.Core;

public sealed class WriteValidator
{
    public void Validate(string sql, string action)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 65536)
            throw new ArgumentException("SQL-teksten må inneholde én kort skrivesetning.", nameof(sql));

        TSql160Parser parser = new(true);
        TSqlFragment fragment = parser.Parse(new StringReader(sql), out IList<ParseError> errors);
        if (errors.Count > 0 || fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count != 1)
            throw new InvalidOperationException("Kun én gyldig SQL-setning er tillatt.");

        TSqlStatement statement = script.Batches[0].Statements[0];
        bool matchesAction = action switch
        {
            "delete" => statement is DeleteStatement,
            "update" => statement is UpdateStatement,
            "insert" => statement is InsertStatement,
            _ => throw new ArgumentException("Ukjent skrivehandling.", nameof(action))
        };
        if (!matchesAction)
            throw new InvalidOperationException($"SQL-setningen må være én {action.ToUpperInvariant()}-setning.");

        ForbiddenWriteVisitor visitor = new(statement);
        fragment.Accept(visitor);
        if (visitor.HasForbiddenConstruct)
            throw new InvalidOperationException("Nøstede skrivekommandoer, SELECT INTO, OUTPUT INTO og sekvensuttrykk er ikke tillatt.");
    }
}
