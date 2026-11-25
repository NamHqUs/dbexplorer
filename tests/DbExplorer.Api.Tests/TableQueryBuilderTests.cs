using System.Data;
using System.Text.Json;
using DbExplorer.Application.Contracts;
using DbExplorer.Infrastructure.Data;

namespace DbExplorer.Api.Tests;

// Pure SQL-generation tests. They need no database.
public sealed class TableQueryBuilderTests
{
    private static readonly TableModel Customers = new(
        "dbo", "Customers", false, null,
        [
            new ColumnModel("Id", 1, "int", false, IsIdentity: true, IsComputed: false, IsRowVersion: false),
            new ColumnModel("Name", 2, "nvarchar(50)", false, false, false, false),
            new ColumnModel("RowVer", 3, "timestamp", false, false, false, true),
        ],
        ["Id"],
        [],
        []);

    [Fact]
    public void Contains_filter_escapes_like_wildcards_so_user_text_matches_literally()
    {
        var request = Request(filters: new Dictionary<string, ColumnFilter>
        {
            ["Name"] = new("text", "contains", Json("\"50%_\""), null, null),
        });

        var query = TableQueryBuilder.Build(Customers, request);

        Assert.Equal("%50\\%\\_%", query.Parameters.Single().Value);
        Assert.Contains("LIKE @p0 ESCAPE", query.Where);
    }

    [Fact]
    public void Filter_on_unknown_column_is_rejected()
    {
        var request = Request(filters: new Dictionary<string, ColumnFilter>
        {
            ["Missing; DROP TABLE x"] = new("text", "equals", Json("\"a\""), null, null),
        });

        Assert.Throws<ArgumentException>(() => TableQueryBuilder.Build(Customers, request));
    }

    [Fact]
    public void Page_size_is_capped_and_primary_key_breaks_sort_ties()
    {
        var request = Request(start: 0, end: 5000, sort: [new SortItem("Name", "desc")]);

        var query = TableQueryBuilder.Build(Customers, request);

        Assert.Equal(1000, query.Take);
        Assert.Equal("[Name] DESC, [Id] ASC", query.OrderBy);
    }

    [Fact]
    public void Insert_rejects_identity_columns()
    {
        var values = new Dictionary<string, JsonElement> { ["Id"] = Json("5") };

        Assert.Throws<ArgumentException>(() => TableWriteBuilder.Insert(Customers, values));
    }

    [Fact]
    public void Update_requires_keys_to_be_exactly_the_primary_key()
    {
        var keys = new Dictionary<string, JsonElement> { ["Name"] = Json("\"x\"") };
        var values = new Dictionary<string, JsonElement> { ["Name"] = Json("\"y\"") };

        Assert.Throws<ArgumentException>(() => TableWriteBuilder.Update(Customers, keys, values, null));
    }

    [Fact]
    public void Update_with_row_version_adds_optimistic_concurrency_check()
    {
        var keys = new Dictionary<string, JsonElement> { ["Id"] = Json("1") };
        var values = new Dictionary<string, JsonElement> { ["Name"] = Json("\"y\"") };
        var rowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 });

        var statement = TableWriteBuilder.Update(Customers, keys, values, rowVersion);

        Assert.Contains("AND [RowVer] = @p2", statement.Sql);
        Assert.Equal(SqlDbType.VarBinary, statement.Parameters.Last().DbType);
    }

    private static TableRowsRequest Request(
        int start = 0,
        int end = 100,
        IReadOnlyList<SortItem>? sort = null,
        IReadOnlyDictionary<string, ColumnFilter>? filters = null) =>
        new(start, end, sort, filters, QuickFilter: null, QuickFilterColumns: null);

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);
}
