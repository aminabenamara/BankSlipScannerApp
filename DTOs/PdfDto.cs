namespace BankSlipScannerApp.DTOs
{
    // ── Résultat complet retourné après traitement du PDF ──
    public class BankSlipResultDto
    {
        public bool Success { get; set; }
        public string PdfType { get; set; } = "";   // "A1" ou "A2"
        public string FileName { get; set; } = "";
        public string? Message { get; set; }

        // Infos du compte
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

        // Période
        public string? DateDebut { get; set; }
        public string? DateFin { get; set; }

        // Transactions extraites
        public List<BankTransactionDto> Transactions { get; set; } = new();

        // Texte brut extrait (pour debug)
        public string? RawText { get; set; }
    }

    // ── Une ligne de transaction ───────────────────────────
    public class BankTransactionDto
    {
        public string? Date { get; set; }
        public string? DateValeur { get; set; }
        public string? Libelle { get; set; }
        public decimal? Debit { get; set; }
        public decimal? Credit { get; set; }
    }

    // ── Résultat détection type seulement ─────────────────
    public class PdfTypeResultDto
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = "";
        public string PdfType { get; set; } = "";
        public string Message { get; set; } = "";
    }
}