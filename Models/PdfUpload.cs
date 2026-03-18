using System.Collections.Generic;

namespace BankSlipScannerApp.Models
{
    public class PdfUpload
    {
        public int Id { get; set; }

        // Infos du fichier
        public string FileName { get; set; } = "";
        public string PdfType { get; set; } = "";

        // Infos bancaires extraites
        public string? IBAN { get; set; }
        public string? RIB { get; set; }
        public string? Compte { get; set; }
        public string? Banque { get; set; }
        public string? Agence { get; set; }
        public string? Devise { get; set; }
        public string? Client { get; set; }

        // Soldes
        public string? SoldeDepart { get; set; }
        public string? SoldeFinal { get; set; }

        // Période du relevé
        public string? DateDebut { get; set; }
        public string? DateFin { get; set; }

        // Nombre de transactions extraites
        public int NbTransactions { get; set; }

        // Statut du traitement PDF
        public bool Success { get; set; }
        public string? Message { get; set; }

        // Date d'upload
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Lié à l'utilisateur
        public int UserId { get; set; }

        // Sprint 3 : Validation
        public string? Statut { get; set; } = "EnAttente";
        public DateTime? ValidatedAt { get; set; }
        public string? RejectReason { get; set; }

        // Navigation (remplace l'ancien "object Transactions")
        public ICollection<PdfTransaction> Transactions { get; set; }
            = new List<PdfTransaction>();
    }
}