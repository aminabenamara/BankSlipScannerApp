using BankSlipScannerApp.DTOs;

namespace BankSlipScannerApp.Services
{
    public interface IValidationService
    {
        Task<ValidationDataDto?> GetValidationDataAsync(int uploadId);
        Task<ValidationResultDto> UpdateTransactionAsync(int transactionId, UpdateTransactionDto dto);
        Task<ValidationResultDto> DeleteTransactionAsync(int transactionId);
        Task<ValidationResultDto> AddTransactionAsync(int uploadId, AddTransactionDto dto);
        Task<ValidationResultDto> ConfirmValidationAsync(int uploadId);
        Task<ValidationResultDto> RejectValidationAsync(int uploadId, string reason);
    }
}