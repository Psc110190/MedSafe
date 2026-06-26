using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MedSafety.API.Models;
using MedSafety.API.Models.External;

namespace MedSafety.API.Services;

/// <summary>
/// Fetches supplemental drug safety data from trusted public APIs:
///   • OpenFDA Drug Label API – official FDA label sections (black box, contraindications, warnings)
///   • NIH RxNorm Interaction API – drug-drug interactions from NLM
///
/// Results are cached in-memory to avoid repeated API calls for the same drug.
/// </summary>
public class ExternalDrugDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalDrugDataService> _logger;
    private readonly IConfiguration _configuration;

    // In-memory caches (thread-safe)
    private readonly ConcurrentDictionary<string, OpenFdaLabelResult?> _fdaLabelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _rxCuiCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<InteractionPair>> _interactionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<DailyMedSection>?> _dailyMedCache = new(StringComparer.OrdinalIgnoreCase);

    private const string OpenFdaBaseUrl = "https://api.fda.gov/drug/label.json";
    private const string RxNormBaseUrl = "https://rxnav.nlm.nih.gov/REST";
    private const string DailyMedBaseUrl = "https://dailymed.nlm.nih.gov/dailymed/services/v2";

    public bool IsEnabled { get; }

    /// <summary>
    /// Maps international / common drug names to their US generic names used by FDA/RxNorm.
    /// OpenFDA uses US names (e.g., "Acetaminophen" not "Paracetamol").
    /// </summary>
    private static readonly Dictionary<string, string> DrugNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Paracetamol"] = "Acetaminophen",
        ["Nurofen"] = "Ibuprofen",
        ["Brufen"] = "Ibuprofen",
        ["Disprin"] = "Aspirin",
        ["Salbutamol"] = "Albuterol",
        ["Adrenaline"] = "Epinephrine",
        ["Noradrenaline"] = "Norepinephrine",
        ["Frusemide"] = "Furosemide",
        ["Glyceryl Trinitrate"] = "Nitroglycerin",
        ["Lignocaine"] = "Lidocaine",
        ["Pethidine"] = "Meperidine",
        ["Prednisolone"] = "Prednisone",
        ["Cephalexin"] = "Cefalexin",
        ["Ciclosporin"] = "Cyclosporine",
        ["Daonil"] = "Glyburide",
        ["Glibenclamide"] = "Glyburide",
        ["Buscopan"] = "Hyoscine butylbromide",
        ["Calpol"] = "Acetaminophen",
        ["Tylenol"] = "Acetaminophen",
        ["Advil"] = "Ibuprofen",
        ["Motrin"] = "Ibuprofen",
    };

    /// <summary>
    /// Resolve a drug name to its US generic name for API lookups.
    /// Falls back to the original name if no alias exists.
    /// </summary>
    public static string ResolveAlias(string drugName)
    {
        var trimmed = drugName.Trim();
        return DrugNameAliases.TryGetValue(trimmed, out var alias) ? alias : trimmed;
    }

    public ExternalDrugDataService(HttpClient httpClient, ILogger<ExternalDrugDataService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
        IsEnabled = _configuration.GetValue("ExternalDrugData:Enabled", true);
    }

    // ──────────────────────────────────────────────────────
    // OpenFDA – Drug Label Data
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetch the FDA drug label for a given generic drug name.
    /// Returns cached data when available.
    /// </summary>
    public async Task<OpenFdaLabelResult?> GetFdaLabelAsync(string drugName)
    {
        if (!IsEnabled) return null;

        // Resolve international names to US generic names
        var resolvedName = ResolveAlias(drugName);
        var key = resolvedName.ToLowerInvariant();
        if (_fdaLabelCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var encodedName = Uri.EscapeDataString(resolvedName);
            var url = $"{OpenFdaBaseUrl}?search=openfda.generic_name:\"{encodedName}\"&limit=1";

            _logger.LogInformation("Fetching OpenFDA label for '{Drug}' (resolved: '{Resolved}') ...", drugName, resolvedName);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenFDA returned {StatusCode} for '{Drug}'", response.StatusCode, drugName);
                _fdaLabelCache[key] = null;
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var fdaResponse = await response.Content.ReadFromJsonAsync<OpenFdaLabelResponse>(options);

            var result = fdaResponse?.Results?.FirstOrDefault();
            if (result != null && !IsOpenFdaLabelMatch(resolvedName, result))
            {
                var returnedName = result.OpenFda?.GenericName?.FirstOrDefault() ??
                    result.OpenFda?.BrandName?.FirstOrDefault() ??
                    result.OpenFda?.SubstanceName?.FirstOrDefault() ??
                    "unknown";
                _logger.LogWarning(
                    "OpenFDA label for '{Drug}' returned non-matching label '{Returned}'. Ignoring result.",
                    drugName,
                    returnedName);
                result = null;
            }

            _fdaLabelCache[key] = result;

            if (result != null)
                _logger.LogInformation("OpenFDA label found for '{Drug}'", drugName);
            else
                _logger.LogInformation("No OpenFDA label results for '{Drug}'", drugName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching OpenFDA label for '{Drug}'", drugName);
            _fdaLabelCache[key] = null;
            return null;
        }
    }

    // ──────────────────────────────────────────────────────
    // RxNorm – Drug-Drug Interactions
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolve a drug name to its RxNorm Concept Unique Identifier (RxCUI).
    /// </summary>
    public async Task<string?> GetRxCuiAsync(string drugName)
    {
        if (!IsEnabled) return null;

        // Resolve international names to US generic names
        var resolvedName = ResolveAlias(drugName);
        var key = resolvedName.ToLowerInvariant();
        if (_rxCuiCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var encodedName = Uri.EscapeDataString(resolvedName);
            var url = $"{RxNormBaseUrl}/rxcui.json?name={encodedName}";

            _logger.LogInformation("Resolving RxCUI for '{Drug}' (resolved: '{Resolved}') ...", drugName, resolvedName);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _rxCuiCache[key] = null;
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rxResponse = await response.Content.ReadFromJsonAsync<RxCuiResponse>(options);

            var rxcui = rxResponse?.IdGroup?.RxnormId?.FirstOrDefault();
            _rxCuiCache[key] = rxcui;

            if (rxcui != null)
                _logger.LogInformation("RxCUI for '{Drug}' = {RxCUI}", drugName, rxcui);
            else
                _logger.LogInformation("No RxCUI found for '{Drug}'", drugName);

            return rxcui;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving RxCUI for '{Drug}'", drugName);
            _rxCuiCache[key] = null;
            return null;
        }
    }

    /// <summary>
    /// Fetch drug-drug interactions from the NIH RxNorm Interaction API for a given RxCUI.
    /// </summary>
    public async Task<List<InteractionPair>> GetInteractionsAsync(string drugName)
    {
        if (!IsEnabled) return new();

        var key = drugName.Trim().ToLowerInvariant();
        if (_interactionCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var rxcui = await GetRxCuiAsync(drugName);
            if (string.IsNullOrEmpty(rxcui))
            {
                _interactionCache[key] = new();
                return new();
            }

            var url = $"{RxNormBaseUrl}/interaction/interaction.json?rxcui={rxcui}";

            _logger.LogInformation("Fetching RxNorm interactions for '{Drug}' (RxCUI={RxCUI}) ...", drugName, rxcui);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _interactionCache[key] = new();
                return new();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var interactionResponse = await response.Content.ReadFromJsonAsync<RxInteractionResponse>(options);

            var pairs = interactionResponse?.InteractionTypeGroup?
                .SelectMany(g => g.InteractionType ?? Enumerable.Empty<InteractionType>())
                .SelectMany(t => t.InteractionPair ?? Enumerable.Empty<InteractionPair>())
                .ToList() ?? new();

            _interactionCache[key] = pairs;
            _logger.LogInformation("Found {Count} interactions for '{Drug}'", pairs.Count, drugName);
            return pairs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching interactions for '{Drug}'", drugName);
            _interactionCache[key] = new();
            return new();
        }
    }

    // ──────────────────────────────────────────────────────
    // OpenFDA – Drug Search (for drugs not in static KB)
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Search OpenFDA for drugs matching a query string.
    /// Returns basic drug info even when not in static knowledge base.
    /// </summary>
    public async Task<List<object>> SearchFdaDrugsAsync(string query)
    {
        if (!IsEnabled) return new();

        try
        {
            // Resolve international names to US generic names for search
            var resolvedQuery = ResolveAlias(query);
            var encodedQuery = Uri.EscapeDataString(resolvedQuery);
            // Search across generic name, brand name, and substance name
            var url = $"{OpenFdaBaseUrl}?search=(openfda.generic_name:\"{encodedQuery}\"+openfda.brand_name:\"{encodedQuery}\"+openfda.substance_name:\"{encodedQuery}\")+AND+_exists_:openfda.generic_name&limit=5";

            _logger.LogInformation("Searching OpenFDA for '{Query}' (resolved: '{Resolved}') ...", query, resolvedQuery);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenFDA search returned {StatusCode} for '{Query}'", response.StatusCode, query);
                return new();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var fdaResponse = await response.Content.ReadFromJsonAsync<OpenFdaLabelResponse>(options);

            var results = fdaResponse?.Results?.Select(r => (object)new
            {
                GenericName = r.OpenFda?.GenericName?.FirstOrDefault() ?? "Unknown",
                BrandNames = r.OpenFda?.BrandName ?? new(),
                DrugClass = r.OpenFda?.PharmClassEpc?.FirstOrDefault() ?? "Unknown",
                Manufacturer = r.OpenFda?.ManufacturerName?.FirstOrDefault() ?? "Unknown",
                Route = r.OpenFda?.Route ?? new(),
                HasBlackBoxWarning = r.BoxedWarning?.Any() == true,
                HasContraindications = r.Contraindications?.Any() == true,
                Indications = TruncateText(r.IndicationsAndUsage?.FirstOrDefault() ?? "", 300),
                Source = "OpenFDA Drug Label API"
            }).ToList() ?? new();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching OpenFDA for '{Query}'", query);
            return new();
        }
    }

    /// <summary>
    /// Build a full safety report for a drug NOT in the static knowledge base,
    /// using only OpenFDA and RxNorm data.
    /// </summary>
    public async Task<DrugSafetyReport?> BuildReportFromExternalAsync(string drugName, PatientProfile patient)
    {
        if (!IsEnabled) return null;

        // Fetch FDA label, interactions and DailyMed SPL concurrently
        var fdaTask = GetFdaLabelAsync(drugName);
        var interactionTask = GetInteractionsAsync(drugName);
        var dailyMedTask = GetDailyMedSectionsAsync(drugName);
        await Task.WhenAll(fdaTask, interactionTask, dailyMedTask);

        var fdaLabel = fdaTask.Result;
        var interactions = interactionTask.Result;
        var dailyMedSections = dailyMedTask.Result;

        if (fdaLabel == null && interactions.Count == 0 && (dailyMedSections == null || dailyMedSections.Count == 0))
            return null; // Not found in any external source either

        var genericName = fdaLabel?.OpenFda?.GenericName?.FirstOrDefault() ?? drugName;
        var drugClass = fdaLabel?.OpenFda?.PharmClassEpc?.FirstOrDefault() ?? "Unknown";

        var report = new DrugSafetyReport
        {
            DrugName = genericName,
            DrugId = drugName.ToLowerInvariant(),
            DrugClass = drugClass
        };

        // Add an informational note that this drug was found via external APIs
        report.Warnings.Add(new SafetyAlert
        {
            Level = AlertLevel.Low,
            Category = "External Data Source",
            Message = $"'{genericName}' was not found in the static knowledge base. Safety data below is sourced from FDA drug labels and NIH RxNorm.",
            Source = "OpenFDA / RxNorm"
        });

        // Enrich from FDA label
        if (fdaLabel != null)
        {
            EnrichFromFdaLabel(report, fdaLabel, patient);

            // For external-only drugs, also extract general contraindications and warnings
            // even when not patient-relevant (user needs to see all info)
            if (fdaLabel.Contraindications?.Any() == true)
            {
                foreach (var ci in fdaLabel.Contraindications)
                {
                    var summary = TruncateText(ci, 400);
                    bool alreadyAdded = report.Warnings.Any(w =>
                        w.Source == "OpenFDA Drug Label" &&
                        w.Category.Contains("CONTRAINDICATION"));

                    if (!alreadyAdded)
                    {
                        report.Warnings.Add(new SafetyAlert
                        {
                            Level = AlertLevel.Moderate,
                            Category = "FDA LABEL - CONTRAINDICATION",
                            Message = $"[{genericName}] {summary}",
                            Source = "OpenFDA Drug Label"
                        });
                    }
                }
            }

            if (fdaLabel.WarningsAndCautions?.Any() == true)
            {
                foreach (var w in fdaLabel.WarningsAndCautions)
                {
                    var summary = TruncateText(w, 400);
                    report.UseWithCaution.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Moderate,
                        Category = "FDA LABEL - WARNINGS & PRECAUTIONS",
                        Message = $"[{genericName}] {summary}",
                        Source = "OpenFDA Drug Label"
                    });
                }
            }
            else if (fdaLabel.Warnings?.Any() == true)
            {
                foreach (var w in fdaLabel.Warnings)
                {
                    var summary = TruncateText(w, 400);
                    report.UseWithCaution.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Moderate,
                        Category = "FDA LABEL - WARNINGS",
                        Message = $"[{genericName}] {summary}",
                        Source = "OpenFDA Drug Label"
                    });
                }
            }
        }

        // Enrich from DailyMed SPL
        if (dailyMedSections != null && dailyMedSections.Count > 0)
        {
            EnrichFromDailyMedSections(report, dailyMedSections, patient);
        }

        // Enrich from RxNorm interactions
        if (interactions.Count > 0 && patient.CurrentMedications.Count > 0)
        {
            EnrichFromRxNormInteractions(report, interactions, patient);
        }

        // Check allergies against the drug name / known allergy groups
        foreach (var allergy in patient.Allergies)
        {
            if (genericName.Contains(allergy, StringComparison.OrdinalIgnoreCase) ||
                drugName.Contains(allergy, StringComparison.OrdinalIgnoreCase))
            {
                report.AllergyAlerts.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "ALLERGY - Possible Match",
                    Message = $"Patient has a known allergy to '{allergy}'. '{genericName}' may be related. Verify before prescribing.",
                    Source = "Allergy Screening (External)"
                });
                report.MustAvoidReasons.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "Allergy Contraindication",
                    Message = $"POSSIBLE ALLERGY MATCH: Patient is allergic to '{allergy}'. '{genericName}' name matches.",
                    Source = "Allergy Screening (External)"
                });
            }
        }

        return report;
    }

    // ──────────────────────────────────────────────────────
    // Combined: Enrich a DrugSafetyReport with external data
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Enrich a DrugSafetyReport with additional data from OpenFDA and RxNorm APIs.
    /// This supplements (not replaces) the static knowledge base.
    /// </summary>
    public async Task EnrichReportAsync(DrugSafetyReport report, PatientProfile patient)
    {
        if (!IsEnabled) return;

        var drugName = report.DrugName;

        // Fetch FDA label and interactions concurrently
        var fdaTask = GetFdaLabelAsync(drugName);
        var interactionTask = GetInteractionsAsync(drugName);
        var dailyMedTask = GetDailyMedSectionsAsync(drugName);

        await Task.WhenAll(fdaTask, interactionTask, dailyMedTask);

        var fdaLabel = fdaTask.Result;
        var interactions = interactionTask.Result;
        var dailyMedSections = dailyMedTask.Result;

        // ── Enrich from FDA label ──
        if (fdaLabel != null)
        {
            EnrichFromFdaLabel(report, fdaLabel, patient);
        }

        // ── Enrich from DailyMed SPL ──
        if (dailyMedSections != null && dailyMedSections.Count > 0)
        {
            EnrichFromDailyMedSections(report, dailyMedSections, patient);
        }

        // ── Enrich drug interactions from RxNorm ──
        if (interactions.Count > 0 && patient.CurrentMedications.Count > 0)
        {
            EnrichFromRxNormInteractions(report, interactions, patient);
        }
    }

    private void EnrichFromFdaLabel(DrugSafetyReport report, OpenFdaLabelResult label, PatientProfile patient)
    {
        var drugName = report.DrugName;

        // Black Box Warnings from FDA label (if not already captured by static data)
        if (label.BoxedWarning?.Any() == true)
        {
            foreach (var bbw in label.BoxedWarning)
            {
                // Truncate very long FDA text to a readable summary (first 500 chars)
                var summary = TruncateText(bbw, 500);

                // Only add if not already present from static data (avoid duplicates)
                if (!report.BlackBoxWarnings.Any(b =>
                    b.Message.Contains(drugName, StringComparison.OrdinalIgnoreCase) &&
                    b.Source == "FDA Black Box Warning"))
                {
                    bool isRelevant = patient.Comorbidities.Any(c =>
                        bbw.Contains(c, StringComparison.OrdinalIgnoreCase));

                    report.BlackBoxWarnings.Add(new SafetyAlert
                    {
                        Level = isRelevant ? AlertLevel.Critical : AlertLevel.High,
                        Category = isRelevant ? "FDA LABEL - BLACK BOX (Relevant)" : "FDA LABEL - BLACK BOX",
                        Message = $"[{drugName}] {summary}",
                        Source = "OpenFDA Drug Label"
                    });
                }
            }
        }

        // Contraindications from FDA label
        if (label.Contraindications?.Any() == true)
        {
            foreach (var ci in label.Contraindications)
            {
                var summary = TruncateText(ci, 400);

                bool isRelevant = patient.Comorbidities.Any(c =>
                    ci.Contains(c, StringComparison.OrdinalIgnoreCase)) ||
                    patient.Allergies.Any(a =>
                    ci.Contains(a, StringComparison.OrdinalIgnoreCase));

                if (isRelevant)
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "FDA LABEL - CONTRAINDICATION (Relevant)",
                        Message = $"[{drugName}] {summary}",
                        Source = "OpenFDA Drug Label"
                    });
                }
            }
        }

        // Pregnancy-specific data from FDA label
        if (patient.IsPregnant && label.Pregnancy?.Any() == true)
        {
            foreach (var preg in label.Pregnancy)
            {
                var summary = TruncateText(preg, 300);
                report.Warnings.Add(new SafetyAlert
                {
                    Level = AlertLevel.High,
                    Category = "FDA LABEL - PREGNANCY",
                    Message = $"[{drugName}] {summary}",
                    Source = "OpenFDA Drug Label"
                });
            }
        }

        // Geriatric use
        if (patient.Age >= 65 && label.GeriatricUse?.Any() == true)
        {
            foreach (var gu in label.GeriatricUse)
            {
                var summary = TruncateText(gu, 300);
                report.UseWithCaution.Add(new SafetyAlert
                {
                    Level = AlertLevel.Moderate,
                    Category = "FDA LABEL - GERIATRIC USE",
                    Message = $"[{drugName}] {summary}",
                    Source = "OpenFDA Drug Label"
                });
            }
        }

        // Pediatric use
        if (patient.Age < 18 && label.PediatricUse?.Any() == true)
        {
            foreach (var pu in label.PediatricUse)
            {
                var summary = TruncateText(pu, 300);
                report.Warnings.Add(new SafetyAlert
                {
                    Level = AlertLevel.High,
                    Category = "FDA LABEL - PEDIATRIC USE",
                    Message = $"[{drugName}] {summary}",
                    Source = "OpenFDA Drug Label"
                });
            }
        }
    }

    // ──────────────────────────────────────────────────────
    // DailyMed – Structured Product Labels (SPL)
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Searches DailyMed for a drug by name and returns its SPL sections.
    /// Returns null if not found or the service is disabled.
    /// </summary>
    public async Task<List<DailyMedSection>?> GetDailyMedSectionsAsync(string drugName)
    {
        if (!IsEnabled) return null;

        var resolvedName = ResolveAlias(drugName);
        var key = resolvedName.ToLowerInvariant();
        if (_dailyMedCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            // Step 1: search for the drug's SetId
            var encodedName = Uri.EscapeDataString(resolvedName);
            var searchUrl = $"{DailyMedBaseUrl}/spls.json?drug_name={encodedName}&pagesize=1&labeltype=human+prescription+drug";

            _logger.LogInformation("Searching DailyMed for '{Drug}' (resolved: '{Resolved}') ...", drugName, resolvedName);

            var searchResp = await _httpClient.GetAsync(searchUrl);
            if (!searchResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("DailyMed search returned {Status} for '{Drug}'", searchResp.StatusCode, drugName);
                _dailyMedCache[key] = null;
                return null;
            }

            if (!IsJsonResponse(searchResp))
            {
                _logger.LogWarning("DailyMed search returned non-JSON content for '{Drug}' ({ContentType})",
                    drugName, searchResp.Content.Headers.ContentType?.MediaType ?? "unknown");
                _dailyMedCache[key] = null;
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var searchResult = await ReadDailyMedJsonAsync<DailyMedSearchResponse>(searchResp, drugName, "search", options);
            var match = searchResult?.Data?.FirstOrDefault(item => IsDailyMedSearchMatch(resolvedName, item));
            var setId = match?.SetId;

            if (string.IsNullOrEmpty(setId))
            {
                _logger.LogInformation("No DailyMed SPL found for '{Drug}'", drugName);
                _dailyMedCache[key] = null;
                return null;
            }

            // Step 2: fetch SPL sections for the SetId
            var sectionsUrl = $"{DailyMedBaseUrl}/spls/{setId}/sections.json";
            var sectionsResp = await _httpClient.GetAsync(sectionsUrl);
            if (!sectionsResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("DailyMed sections returned {Status} for '{Drug}'", sectionsResp.StatusCode, drugName);
                _dailyMedCache[key] = null;
                return null;
            }

            if (!IsJsonResponse(sectionsResp))
            {
                _logger.LogInformation("DailyMed sections returned non-JSON content for '{Drug}' ({ContentType})",
                    drugName, sectionsResp.Content.Headers.ContentType?.MediaType ?? "unknown");
                _dailyMedCache[key] = null;
                return null;
            }

            var splData = await ReadDailyMedJsonAsync<DailyMedSectionsResponse>(sectionsResp, drugName, "sections", options);
            var sections = splData?.Data?.Sections;

            _dailyMedCache[key] = sections;

            if (sections != null)
                _logger.LogInformation("DailyMed SPL found for '{Drug}' – {Count} sections", drugName, sections.Count);

            return sections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching DailyMed SPL for '{Drug}'", drugName);
            _dailyMedCache[key] = null;
            return null;
        }
    }

    private static bool IsJsonResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsOpenFdaLabelMatch(string requestedName, OpenFdaLabelResult result)
    {
        var candidateNames = (result.OpenFda?.GenericName ?? new())
            .Concat(result.OpenFda?.BrandName ?? new())
            .Concat(result.OpenFda?.SubstanceName ?? new());

        return candidateNames.Any(candidate => NamesMatch(requestedName, candidate));
    }

    private static bool IsDailyMedSearchMatch(string requestedName, DailyMedSplItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title)) return false;
        return NamesMatch(requestedName, item.Title);
    }

    private static bool NamesMatch(string requestedName, string candidateName)
    {
        var requested = NormalizeDrugName(requestedName);
        var candidate = NormalizeDrugName(candidateName);

        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
            return false;

        if (candidate.Equals(requested, StringComparison.OrdinalIgnoreCase))
            return true;

        var requestedTokens = requested
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedTokens.Count == 0)
            return false;

        if (requestedTokens.Count == 1 && IsAmbiguousSingleTokenLookup(requestedTokens[0]))
            return false;

        var candidateTokens = candidate
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requestedTokens.All(candidateTokens.Contains);
    }

    private static bool IsAmbiguousSingleTokenLookup(string token)
    {
        var ambiguousLookupTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "drug",
            "fake",
            "medicine",
            "medication",
            "sample",
            "test",
            "unknown"
        };

        return ambiguousLookupTokens.Contains(token);
    }

    private static string NormalizeDrugName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return System.Text.RegularExpressions.Regex.Replace(new string(chars), @"\s+", " ").Trim();
    }

    private async Task<T?> ReadDailyMedJsonAsync<T>(
        HttpResponseMessage response,
        string drugName,
        string stage,
        JsonSerializerOptions options)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "DailyMed {Stage} returned invalid JSON for '{Drug}'", stage, drugName);
            return default;
        }
    }

    private void EnrichFromDailyMedSections(DrugSafetyReport report, List<DailyMedSection> sections, PatientProfile patient)
    {
        var drugName = report.DrugName;

        // Section name keywords to look for (case-insensitive)
        foreach (var section in sections)
        {
            var sectionName = section.Name?.ToUpperInvariant() ?? string.Empty;
            var rawValue = section.Value ?? string.Empty;

            // Strip HTML tags for readable text
            var text = System.Text.RegularExpressions.Regex.Replace(rawValue, "<[^>]+>", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text)) continue;

            // ── Boxed / Black Box Warning ──
            if (sectionName.Contains("BOXED WARNING") || sectionName.Contains("BLACK BOX"))
            {
                var summary = TruncateText(text, 500);
                if (!report.BlackBoxWarnings.Any(b => b.Source == "DailyMed SPL"))
                {
                    report.BlackBoxWarnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "DAILYMED - BLACK BOX WARNING",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Contraindications ──
            else if (sectionName.Contains("CONTRAINDICATION"))
            {
                var summary = TruncateText(text, 400);
                bool isRelevant = patient.Comorbidities.Any(c =>
                    text.Contains(c, StringComparison.OrdinalIgnoreCase)) ||
                    patient.Allergies.Any(a =>
                    text.Contains(a, StringComparison.OrdinalIgnoreCase));

                if (isRelevant && !report.Warnings.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("CONTRAINDICATION")))
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "DAILYMED - CONTRAINDICATION (Relevant)",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Warnings & Precautions ──
            else if (sectionName.Contains("WARNINGS AND PRECAUTIONS") || sectionName == "WARNINGS")
            {
                var summary = TruncateText(text, 400);
                bool isRelevant = patient.Comorbidities.Any(c =>
                    text.Contains(c, StringComparison.OrdinalIgnoreCase));

                if (isRelevant && !report.UseWithCaution.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("WARNINGS")))
                {
                    report.UseWithCaution.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Moderate,
                        Category = "DAILYMED - WARNINGS & PRECAUTIONS (Relevant)",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Drug Interactions ──
            else if (sectionName.Contains("DRUG INTERACTION"))
            {
                foreach (var currentMed in patient.CurrentMedications)
                {
                    if (text.Contains(currentMed, StringComparison.OrdinalIgnoreCase))
                    {
                        var alreadyReported = report.DrugInteractions.Any(di =>
                            di.CurrentDrug.Equals(currentMed, StringComparison.OrdinalIgnoreCase) &&
                            di.ProposedDrug.Equals(drugName, StringComparison.OrdinalIgnoreCase) &&
                            di.Mechanism.Contains("DailyMed"));

                        if (!alreadyReported)
                        {
                            var snippet = TruncateText(text, 350);
                            report.DrugInteractions.Add(new InteractionAlert
                            {
                                Level = AlertLevel.Moderate,
                                CurrentDrug = currentMed,
                                ProposedDrug = drugName,
                                Effect = snippet,
                                Mechanism = "Identified via DailyMed SPL Drug Interactions section",
                                Management = "Review DailyMed label for full interaction details."
                            });
                        }
                    }
                }
            }
            // ── Pregnancy ──
            else if (patient.IsPregnant && (sectionName.Contains("PREGNANCY") || sectionName.Contains("SPECIFIC POPULATION")))
            {
                var summary = TruncateText(text, 300);
                if (!report.Warnings.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("PREGNANCY")))
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "DAILYMED - PREGNANCY",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Nursing / Lactation ──
            else if (patient.IsBreastfeeding && (sectionName.Contains("NURSING") || sectionName.Contains("LACTATION")))
            {
                var summary = TruncateText(text, 300);
                if (!report.Warnings.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("NURSING")))
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "DAILYMED - NURSING MOTHERS",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Geriatric Use ──
            else if (patient.Age >= 65 && sectionName.Contains("GERIATRIC"))
            {
                var summary = TruncateText(text, 300);
                if (!report.UseWithCaution.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("GERIATRIC")))
                {
                    report.UseWithCaution.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Moderate,
                        Category = "DAILYMED - GERIATRIC USE",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
            // ── Pediatric Use ──
            else if (patient.Age < 18 && sectionName.Contains("PEDIATRIC"))
            {
                var summary = TruncateText(text, 300);
                if (!report.Warnings.Any(w => w.Source == "DailyMed SPL" && w.Category.Contains("PEDIATRIC")))
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "DAILYMED - PEDIATRIC USE",
                        Message = $"[{drugName}] {summary}",
                        Source = "DailyMed SPL"
                    });
                }
            }
        }
    }

    private void EnrichFromRxNormInteractions(DrugSafetyReport report, List<InteractionPair> interactions, PatientProfile patient)
    {
        var drugName = report.DrugName;

        foreach (var currentMed in patient.CurrentMedications)
        {
            var relevantPairs = interactions.Where(pair =>
                pair.InteractionConcept?.Any(ic =>
                    ic.MinConceptItem?.Name?.Contains(currentMed, StringComparison.OrdinalIgnoreCase) == true ||
                    ic.SourceConceptItem?.Name?.Contains(currentMed, StringComparison.OrdinalIgnoreCase) == true
                ) == true
            ).ToList();

            foreach (var pair in relevantPairs)
            {
                var description = TruncateText(pair.Description ?? "Interaction detected", 400);

                // Avoid duplicate if static data already caught this interaction
                var alreadyReported = report.DrugInteractions.Any(di =>
                    di.CurrentDrug.Equals(currentMed, StringComparison.OrdinalIgnoreCase) &&
                    di.ProposedDrug.Equals(drugName, StringComparison.OrdinalIgnoreCase));

                if (!alreadyReported)
                {
                    var severity = MapRxNormSeverity(pair.Severity);

                    report.DrugInteractions.Add(new InteractionAlert
                    {
                        Level = severity,
                        CurrentDrug = currentMed,
                        ProposedDrug = drugName,
                        Effect = description,
                        Mechanism = "Identified via NIH RxNorm Interaction API",
                        Management = severity == AlertLevel.Critical
                            ? "Avoid combination or consult pharmacist/specialist."
                            : "Monitor patient closely. Adjust dosage if needed."
                    });

                    if (severity == AlertLevel.Critical)
                    {
                        report.MustAvoidReasons.Add(new SafetyAlert
                        {
                            Level = AlertLevel.Critical,
                            Category = "MAJOR Drug Interaction (RxNorm)",
                            Message = $"MAJOR INTERACTION: {drugName} + {currentMed} → {description}",
                            Source = "NIH RxNorm Interaction API"
                        });
                    }
                }
            }
        }
    }

    private static AlertLevel MapRxNormSeverity(string? severity)
    {
        if (string.IsNullOrEmpty(severity))
            return AlertLevel.Moderate;

        return severity.ToLowerInvariant() switch
        {
            "high" => AlertLevel.Critical,
            "n/a" => AlertLevel.Moderate,
            _ => AlertLevel.High
        };
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Clean up extra whitespace
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");

        if (cleaned.Length <= maxLength)
            return cleaned;

        return cleaned[..maxLength] + "...";
    }
}
