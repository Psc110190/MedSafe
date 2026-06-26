using System.Text.Json;
using System.Text.Json.Serialization;
using MedSafety.API.Models;

namespace MedSafety.API.Services;

public class CustomSafetyRuleService
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private List<CustomSafetyRule> _rules = new();

    public CustomSafetyRuleService(IHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "custom-safety-rules.json");
        _rules = LoadRules();
    }

    public IReadOnlyList<CustomSafetyRule> GetAll()
    {
        lock (_sync)
        {
            return _rules
                .OrderByDescending(r => r.Enabled)
                .ThenBy(r => r.Name)
                .Select(Clone)
                .ToList();
        }
    }

    public CustomSafetyRule? GetById(string id)
    {
        lock (_sync)
        {
            var rule = _rules.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return rule == null ? null : Clone(rule);
        }
    }

    public CustomSafetyRule Create(UpsertCustomSafetyRuleRequest request)
    {
        var now = DateTime.UtcNow;
        var rule = FromRequest(request);
        rule.Id = Guid.NewGuid().ToString("N");
        rule.CreatedAt = now;
        rule.UpdatedAt = now;

        lock (_sync)
        {
            _rules.Add(rule);
            SaveRules();
            return Clone(rule);
        }
    }

    public CustomSafetyRule? Update(string id, UpsertCustomSafetyRuleRequest request)
    {
        lock (_sync)
        {
            var index = _rules.FindIndex(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;

            var existing = _rules[index];
            var updated = FromRequest(request);
            updated.Id = existing.Id;
            updated.CreatedAt = existing.CreatedAt;
            updated.UpdatedAt = DateTime.UtcNow;
            _rules[index] = updated;
            SaveRules();
            return Clone(updated);
        }
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            var removed = _rules.RemoveAll(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) SaveRules();
            return removed;
        }
    }

    public static List<string> CleanTerms(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> Validate(UpsertCustomSafetyRuleRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Rule name is required.");
        if (string.IsNullOrWhiteSpace(request.Category))
            errors.Add("Category is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            errors.Add("Message is required.");
        if (string.IsNullOrWhiteSpace(request.SuggestedAction))
            errors.Add("Suggested action is required.");
        if (CleanTerms(request.MedicationTerms).Count == 0)
            errors.Add("At least one medication term is required.");

        var hasPatientTrigger =
            CleanTerms(request.ConditionTerms).Count > 0 ||
            CleanTerms(request.AllergyTerms).Count > 0 ||
            CleanTerms(request.SymptomTerms).Count > 0 ||
            CleanTerms(request.RiskFlagTerms).Count > 0;

        if (!hasPatientTrigger)
            errors.Add("Add at least one condition, allergy, symptom, or risk-flag trigger.");

        return errors;
    }

    private List<CustomSafetyRule> LoadRules()
    {
        if (!File.Exists(_filePath))
            return new();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<CustomSafetyRule>>(json, _jsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveRules()
    {
        var json = JsonSerializer.Serialize(_rules, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static CustomSafetyRule FromRequest(UpsertCustomSafetyRuleRequest request)
    {
        return new CustomSafetyRule
        {
            Name = request.Name.Trim(),
            Enabled = request.Enabled,
            Level = request.Level,
            Category = request.Category.Trim(),
            MedicationTerms = CleanTerms(request.MedicationTerms),
            ConditionTerms = CleanTerms(request.ConditionTerms),
            AllergyTerms = CleanTerms(request.AllergyTerms),
            SymptomTerms = CleanTerms(request.SymptomTerms),
            RiskFlagTerms = CleanTerms(request.RiskFlagTerms),
            Message = request.Message.Trim(),
            SuggestedAction = request.SuggestedAction.Trim(),
            Source = string.IsNullOrWhiteSpace(request.Source) ? "Custom rule" : request.Source.Trim()
        };
    }

    private static CustomSafetyRule Clone(CustomSafetyRule rule)
    {
        return new CustomSafetyRule
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            Level = rule.Level,
            Category = rule.Category,
            MedicationTerms = rule.MedicationTerms.ToList(),
            ConditionTerms = rule.ConditionTerms.ToList(),
            AllergyTerms = rule.AllergyTerms.ToList(),
            SymptomTerms = rule.SymptomTerms.ToList(),
            RiskFlagTerms = rule.RiskFlagTerms.ToList(),
            Message = rule.Message,
            SuggestedAction = rule.SuggestedAction,
            Source = rule.Source,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }
}
