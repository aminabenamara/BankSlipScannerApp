using System.ComponentModel.DataAnnotations;

namespace BankSlipScannerApp.DTOs
{
    // ── Retour validation ────────────────────────────────────────
    public class ValidationDataDto
    {
        public int UploadId { get; set; }
        public string FileName { get; set; } = "";
        public string PdfType { get; set; } = "";
        public string Banque { get; set; } = "";
        public string? Agence { get; set; }
        public string? IBAN { get; set; }
        public string? RIB { get; set; }
        public string? Devise { get; set; }
        public string? Client { get; set; }
        public string? SoldeDepart { get; set; }
        public string? SoldeFinal { get; set; }
        public string? DateDebut { get; set; }
        public string? DateFin { get; set; }
        public string Statut { get; set; } = "EnAttente";
        public List<TransactionValidationDto> Transactions { get; set; } = new();

        // Résumé calculé
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public int NbTransactions { get; set; }

        // Anomalies détectées automatiquement
        public List<AnomalieDto> Anomalies { get; set; } = new();
    }

    public class TransactionValidationDto
    {
        public int Id { get; set; }
        public string Date { get; set; } = "";
        public string Libelle { get; set; } = "";
        public string? DateValeur { get; set; }
        public decimal? Debit { get; set; }
        public decimal? Credit { get; set; }
        public bool HasAnomalie { get; set; }
        public string? AnomalieMessage { get; set; }
        public bool IsModified { get; set; }
    }

    public class AnomalieDto
    {
        public int TransactionId { get; set; }
        public string Type { get; set; } = ""; // "MontantZero", "DateInvalide", "LibelleVide"
        public string Message { get; set; } = "";
        public string Severite { get; set; } = "Warning"; // "Warning" | "Error"
    }

    // ── Modifier transaction ─────────────────────────────────────
    public class UpdateTransactionDto
    {
        [Required]
        public string Date { get; set; } = "";

        [Required]
        [MinLength(2)]
        public string Libelle { get; set; } = "";

        public string? DateValeur { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? Debit { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? Credit { get; set; }
    }

    // ── Ajouter transaction ──────────────────────────────────────
    public class AddTransactionDto
    {
        [Required]
        public string Date { get; set; } = "";

        [Required]
        [MinLength(2)]
        public string Libelle { get; set; } = "";

        public string? DateValeur { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? Debit { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? Credit { get; set; }
    }

    // ── Rejeter ──────────────────────────────────────────────────
    public class RejectDto
    {
        public string Reason { get; set; } = "";
    }

    // ── Résultat action ──────────────────────────────────────────
    public class ValidationResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }
    }
}