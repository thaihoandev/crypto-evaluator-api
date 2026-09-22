using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoEvaluator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitCodeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Timeframe = table.Column<int>(type: "integer", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    StopLoss = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    TakeProfit = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    AccountBalance = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    RiskPercent = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Leverage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Indicator = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Operator = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategy_rules_strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Ema20 = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Ema50 = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Ema200 = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Rsi = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Atr = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Volume = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    AverageVolume = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    VolumeRatio = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_market_snapshots_trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RiskRewardRatio = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    RiskAmount = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    PositionSize = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    MarginRequired = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_evaluations_trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_predictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinProbability = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    LossProbability = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    NoHitProbability = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ExpectedRMultiple = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: true),
                    RandomSeed = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_predictions_trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Pnl = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    RMultiple = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    IsWin = table.Column<bool>(type: "boolean", nullable: false),
                    ExitReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_results_trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_snapshots_TradeId",
                table: "market_snapshots",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_strategy_rules_StrategyId",
                table: "strategy_rules",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_evaluations_TradeId",
                table: "trade_evaluations",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_predictions_TradeId",
                table: "trade_predictions",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_results_TradeId",
                table: "trade_results",
                column: "TradeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_snapshots");

            migrationBuilder.DropTable(
                name: "strategy_rules");

            migrationBuilder.DropTable(
                name: "trade_evaluations");

            migrationBuilder.DropTable(
                name: "trade_predictions");

            migrationBuilder.DropTable(
                name: "trade_results");

            migrationBuilder.DropTable(
                name: "strategies");

            migrationBuilder.DropTable(
                name: "trades");
        }
    }
}
