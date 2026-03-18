using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankSlipScannerApp.Controllers
{
    [ApiController]
    [Route("api/validation")]
    public class ValidationController : ControllerBase
    {
        private readonly IValidationService _validationService;

        public ValidationController(IValidationService validationService)
        {
            _validationService = validationService;
        }

        // GET /api/validation/{uploadId}
        // Retourne les transactions d'un upload pour validation
        [HttpGet("{uploadId}")]
        public async Task<IActionResult> GetForValidation(int uploadId)
        {
            var result = await _validationService.GetValidationDataAsync(uploadId);
            if (result == null)
                return NotFound(new { message = "Upload introuvable." });
            return Ok(result);
        }

        // PUT /api/validation/transaction/{transactionId}
        // Modifier une transaction (libellé, montant, date)
        [HttpPut("transaction/{transactionId}")]
        public async Task<IActionResult> UpdateTransaction(
            int transactionId,
            [FromBody] UpdateTransactionDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _validationService.UpdateTransactionAsync(transactionId, dto);
            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(result);
        }

        // DELETE /api/validation/transaction/{transactionId}
        // Supprimer une transaction
        [HttpDelete("transaction/{transactionId}")]
        public async Task<IActionResult> DeleteTransaction(int transactionId)
        {
            var result = await _validationService.DeleteTransactionAsync(transactionId);
            if (!result.Success)
                return BadRequest(new { message = result.Message });
            return Ok(result);
        }

        // POST /api/validation/{uploadId}/transaction
        // Ajouter une transaction manquante
        [HttpPost("{uploadId}/transaction")]
        public async Task<IActionResult> AddTransaction(
            int uploadId,
            [FromBody] AddTransactionDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _validationService.AddTransactionAsync(uploadId, dto);
            if (!result.Success)
                return BadRequest(new { message = result.Message });
            return Ok(result);
        }

        // POST /api/validation/{uploadId}/confirm
        // Confirmer toutes les transactions → statut "Validé"
        [HttpPost("{uploadId}/confirm")]
        public async Task<IActionResult> ConfirmValidation(int uploadId)
        {
            var result = await _validationService.ConfirmValidationAsync(uploadId);
            if (!result.Success)
                return BadRequest(new { message = result.Message });
            return Ok(result);
        }

        // POST /api/validation/{uploadId}/reject
        // Rejeter → statut "Rejeté"
        [HttpPost("{uploadId}/reject")]
        public async Task<IActionResult> RejectValidation(
            int uploadId,
            [FromBody] RejectDto dto)
        {
            var result = await _validationService.RejectValidationAsync(uploadId, dto.Reason);
            if (!result.Success)
                return BadRequest(new { message = result.Message });
            return Ok(result);
        }
    }
}