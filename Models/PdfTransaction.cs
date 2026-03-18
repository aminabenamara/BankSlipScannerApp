namespace BankSlipScannerApp.Models
{
    public class PdfTransaction
    {
        public int Id { get; set; }

        // Données de la transaction
        public string? Date { get; set; }
        public string? DateValeur { get; set; }
        public string? Libelle { get; set; }
        public decimal? Debit { get; set; }
        public decimal? Credit { get; set; }

        // Lié au PdfUpload
        public int PdfUploadId { get; set; }
        public PdfUpload? PdfUpload { get; set; }
        // lié Validation
        public bool IsModified { get; set; } = false;
        public DateTime? ModifiedAt { get; set; }
    }

}