using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Goals",
                type: "TEXT",
                nullable: true);

            // Goals completed before this column existed: use the date of their last contribution
            // (the one that completed them), or the creation date if they somehow have none.
            migrationBuilder.Sql(@"
                UPDATE ""Goals""
                SET ""CompletedAt"" = COALESCE(
                    (SELECT MAX(""Date"") FROM ""GoalContributions"" WHERE ""GoalContributions"".""GoalId"" = ""Goals"".""Id""),
                    ""CreatedAt"")
                WHERE ""IsCompleted"" = 1 AND ""CompletedAt"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Goals");
        }
    }
}
