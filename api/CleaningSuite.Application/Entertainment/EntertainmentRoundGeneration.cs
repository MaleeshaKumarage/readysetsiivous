using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

internal static class EntertainmentRoundGeneration
{
    public static async Task<EntertainmentRound> GenerateAsync(IEntertainmentRepository repository, IGeminiSentenceService provider,
        EntertainmentGroup group, EntertainmentPlayer translator, int number, CancellationToken ct)
    {
        var history = (await repository.ListAllSentencesAsync(group.Id, ct) ?? Array.Empty<string>()).ToList();
        var promptHistory = history.Take(20).ToList();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var sentence = await provider.GenerateAsync(group.Description, translator.Level, string.Empty, promptHistory, ct);
            ct.ThrowIfCancellationRequested();
            if (sentence.IsSample) throw new EntertainmentAiUnavailableException();
            if (EntertainmentSentenceIdentity.IsDuplicate(sentence.SentenceEn, history))
            {
                promptHistory = new[] { $"REJECTED DUPLICATE: {sentence.SentenceEn}. Generate a substantially different sentence." }
                    .Concat(history.Take(19)).ToList();
                continue;
            }
            var round = EntertainmentRound.Create(group.Id, number, translator.Id, sentence.SentenceEn, sentence.SentenceFi, sentence.BaseForms);
            round.Hints = sentence.Hints.ToList();
            var oldId = group.CurrentRoundId;
            var oldStatus = group.Status;
            var oldUpdated = group.UpdatedUtc;
            group.CurrentRoundId = round.Id;
            group.Status = EntertainmentStatus.InGame;
            group.UpdatedUtc = DateTime.UtcNow;
            try
            {
                await repository.SaveGeneratedRoundAsync(group, round, ct);
                return round;
            }
            catch (EntertainmentDuplicateSentenceException)
            {
                group.CurrentRoundId = oldId;
                group.Status = oldStatus;
                group.UpdatedUtc = oldUpdated;
                history.Add(sentence.SentenceEn);
                promptHistory = new[] { $"REJECTED DUPLICATE: {sentence.SentenceEn}. Generate a substantially different sentence." }
                    .Concat(history.Take(19)).ToList();
            }
            catch
            {
                group.CurrentRoundId = oldId;
                group.Status = oldStatus;
                group.UpdatedUtc = oldUpdated;
                throw;
            }
        }
        throw new EntertainmentAiUnavailableException("Could not generate a new question. Please retry.");
    }
}
