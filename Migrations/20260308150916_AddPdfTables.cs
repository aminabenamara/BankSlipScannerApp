using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankSlipScannerApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PdfUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RIB = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Compte = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Banque = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Agence = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Devise = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Client = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SoldeDepart = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SoldeFinal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateDebut = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateFin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NbTransactions = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PdfTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateValeur = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Libelle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PdfUploadId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdfTransactions_PdfUploads_PdfUploadId",
                        column: x => x.PdfUploadId,
                        principalTable: "PdfUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PdfTransactions_PdfUploadId",
                table: "PdfTransactions",
                column: "PdfUploadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PdfTransactions");

            migrationBuilder.DropTable(
                name: "PdfUploads");
        }
    }
}
