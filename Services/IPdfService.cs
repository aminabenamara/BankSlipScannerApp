using BankSlipScannerApp.DTOs;

namespace BankSlipScannerApp.Services
{
    public interface IPdfService
    {
        // Traiter le PDF complet → retourne toutes les données
        Task<BankSlipResultDto> ProcessPdfAsync(byte[] pdfBytes, string fileName);

        // Détecter seulement le type A1 ou A2
        Task<string> DetectPdfTypeAsync(byte[] pdfBytes);

        Task<string> GetRawTextAsync(byte[] pdfBytes);
    }
}