using System.Text.RegularExpressions;
using BankSlipScannerApp.Data;
using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BankSlipScannerApp.Services
{
    public class ValidationService : IValidationService
    {
        private readonly AppDbContext _context;

        public ValidationService(AppDbContext context)
        {
            _context = context;
        }

        
        // GET VALIDATION DATA
        
        public async Task<ValidationDataDto?> GetValidationDataAsync(int uploadId)
        {
            var upload = await _context.PdfUploads
                .Include(u => u.Transactions)
                .FirstOrDefaultAsync(u => u.Id == uploadId);

            if (upload == null) return null;

            var transactions = upload.Transactions
                .OrderBy(t => t.Date)
                .Select(t => new TransactionValidationDto
                {
                    Id = t.Id,
                    Date = t.Date ?? "",
                    Libelle = t.Libelle ?? "",
                    DateValeur = t.DateValeur,
                    Debit = t.Debit,
                    Credit = t.Credit,
                    IsModified = t.IsModified
                })
                .ToList();

            // Détecter anomalies automatiquement
            var anomalies = DetectAnomalies(transactions);

            // Marquer transactions avec anomalies
            foreach (var tx in transactions)
            {
                var txAnomalies = anomalies.Where(a => a.TransactionId == tx.Id).ToList();
                if (txAnomalies.Any())
                {
                    tx.HasAnomalie = true;
                    tx.AnomalieMessage = string.Join(" | ", txAnomalies.Select(a => a.Message));
                }
            }

            return new ValidationDataDto
            {
                UploadId = upload.Id,
                FileName = upload.FileName ?? "",
                PdfType = upload.PdfType ?? "",
                Banque = upload.Banque ?? "",
                Agence = upload.Agence,
                IBAN = upload.IBAN,
                RIB = upload.RIB,
                Devise = upload.Devise,
                Client = upload.Client,
                SoldeDepart = upload.SoldeDepart,
                SoldeFinal = upload.SoldeFinal,
                DateDebut = upload.DateDebut,
                DateFin = upload.DateFin,
                Statut = upload.Statut ?? "EnAttente",
                Transactions = transactions,
                TotalDebit = transactions.Sum(t => t.Debit ?? 0),
                TotalCredit = transactions.Sum(t => t.Credit ?? 0),
                NbTransactions = transactions.Count,
                Anomalies = anomalies
            };
        }

        
        // DÉTECTION ANOMALIES
      
        private List<AnomalieDto> DetectAnomalies(List<TransactionValidationDto> transactions)
        {
            var anomalies = new List<AnomalieDto>();

            foreach (var tx in transactions)
            {
                // 1. Libellé vide ou trop court
                if (string.IsNullOrWhiteSpace(tx.Libelle) || tx.Libelle.Length < 3)
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "LibelleVide",
                        Message = "Libellé manquant ou trop court",
                        Severite = "Error"
                    });

                // 2. Débit ET Crédit tous les deux nuls
                if ((tx.Debit == null || tx.Debit == 0) &&
                    (tx.Credit == null || tx.Credit == 0))
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "MontantZero",
                        Message = "Débit et Crédit tous les deux à zéro",
                        Severite = "Error"
                    });

                // 3. Débit ET Crédit tous les deux renseignés (impossible normalement)
                if (tx.Debit > 0 && tx.Credit > 0)
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "DebitCreditDouble",
                        Message = "Débit et Crédit renseignés simultanément",
                        Severite = "Warning"
                    });

                // 4. Date invalide
                if (!string.IsNullOrEmpty(tx.Date) &&
                    !Regex.IsMatch(tx.Date, @"\d{2}[/\-]\d{2}[/\-]\d{4}|\d{2}\s+\d{2}\s+\d{4}"))
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "DateInvalide",
                        Message = $"Format de date invalide : '{tx.Date}'",
                        Severite = "Warning"
                    });

                // 5. Montant négatif
                if (tx.Debit < 0 || tx.Credit < 0)
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "MontantNegatif",
                        Message = "Montant négatif détecté",
                        Severite = "Error"
                    });

                // 6. Montant anormalement élevé (> 1 000 000)
                if (tx.Debit > 1_000_000 || tx.Credit > 1_000_000)
                    anomalies.Add(new AnomalieDto
                    {
                        TransactionId = tx.Id,
                        Type = "MontantElevé",
                        Message = "Montant anormalement élevé (> 1 000 000)",
                        Severite = "Warning"
                    });
            }

            return anomalies;
        }

        
        // UPDATE TRANSACTION
        
        public async Task<ValidationResultDto> UpdateTransactionAsync(
            int transactionId, UpdateTransactionDto dto)
        {
            var tx = await _context.PdfTransactions.FindAsync(transactionId);
            if (tx == null)
                return new ValidationResultDto { Success = false, Message = "Transaction introuvable." };

            tx.Date = dto.Date;
            tx.Libelle = dto.Libelle;
            tx.DateValeur = dto.DateValeur;
            tx.Debit = dto.Debit;
            tx.Credit = dto.Credit;
            tx.IsModified = true;
            tx.ModifiedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new ValidationResultDto
            {
                Success = true,
                Message = "Transaction mise à jour.",
                Data = new { tx.Id, tx.Date, tx.Libelle, tx.Debit, tx.Credit }
            };
        }

        // DELETE TRANSACTION
        
        public async Task<ValidationResultDto> DeleteTransactionAsync(int transactionId)
        {
            var tx = await _context.PdfTransactions.FindAsync(transactionId);
            if (tx == null)
                return new ValidationResultDto { Success = false, Message = "Transaction introuvable." };

            _context.PdfTransactions.Remove(tx);
            await _context.SaveChangesAsync();

            return new ValidationResultDto { Success = true, Message = "Transaction supprimée." };
        }

        
        // ADD TRANSACTION
        
        public async Task<ValidationResultDto> AddTransactionAsync(
            int uploadId, AddTransactionDto dto)
        {
            var upload = await _context.PdfUploads.FindAsync(uploadId);
            if (upload == null)
                return new ValidationResultDto { Success = false, Message = "Upload introuvable." };

            var tx = new PdfTransaction
            {
                PdfUploadId = uploadId,
                Date = dto.Date,
                Libelle = dto.Libelle,
                DateValeur = dto.DateValeur,
                Debit = dto.Debit,
                Credit = dto.Credit,
                IsModified = true,
                ModifiedAt = DateTime.UtcNow
            };

            _context.PdfTransactions.Add(tx);

            // Mettre à jour le compteur
            upload.NbTransactions = upload.NbTransactions + 1;

            await _context.SaveChangesAsync();

            return new ValidationResultDto
            {
                Success = true,
                Message = "Transaction ajoutée.",
                Data = new { tx.Id, tx.Date, tx.Libelle, tx.Debit, tx.Credit }
            };
        }

        
        // CONFIRM VALIDATION
       
        public async Task<ValidationResultDto> ConfirmValidationAsync(int uploadId)
        {
            var upload = await _context.PdfUploads
                .Include(u => u.Transactions)
                .FirstOrDefaultAsync(u => u.Id == uploadId);

            if (upload == null)
                return new ValidationResultDto { Success = false, Message = "Upload introuvable." };

            // Vérifier qu'il n'y a plus d'anomalies bloquantes (Error)
            var transactions = upload.Transactions
                .Select(t => new TransactionValidationDto
                {
                    Id = t.Id,
                    Date = t.Date ?? "",
                    Libelle = t.Libelle ?? "",
                    Debit = t.Debit,
                    Credit = t.Credit
                }).ToList();

            var anomalies = DetectAnomalies(transactions)
                .Where(a => a.Severite == "Error").ToList();

            if (anomalies.Any())
                return new ValidationResultDto
                {
                    Success = false,
                    Message = $"{anomalies.Count} erreur(s) bloquante(s) à corriger avant validation.",
                    Data = anomalies
                };

            upload.Statut = "Validé";
            upload.ValidatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new ValidationResultDto
            {
                Success = true,
                Message = $"Relevé validé avec succès. {upload.Transactions.Count} transaction(s) confirmée(s)."
            };
        }

       
        // REJECT VALIDATION
        
        public async Task<ValidationResultDto> RejectValidationAsync(int uploadId, string reason)
        {
            var upload = await _context.PdfUploads.FindAsync(uploadId);
            if (upload == null)
                return new ValidationResultDto { Success = false, Message = "Upload introuvable." };

            upload.Statut = "Rejeté";
            upload.RejectReason = reason;
            upload.ValidatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new ValidationResultDto { Success = true, Message = "Relevé rejeté." };
        }
    }
}