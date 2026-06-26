using Microsoft.AspNetCore.Mvc;
using MedSafety.API.Models;
using MedSafety.API.Services;

namespace MedSafety.API.Controllers;

/// <summary>
/// Endpoints for browsing the medication knowledge base.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DrugsController : ControllerBase
{
    private readonly MedicationSafetyService _safetyService;
    private readonly ExternalDrugDataService _externalService;

    public DrugsController(MedicationSafetyService safetyService, ExternalDrugDataService externalService)
    {
        _safetyService = safetyService;
        _externalService = externalService;
    }

    /// <summary>
    /// Get all drugs in the knowledge base.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public ActionResult GetAllDrugs()
    {
        var drugs = _safetyService.GetAllDrugs();
        var summary = drugs.Select(d => new
        {
            d.DrugId,
            d.GenericName,
            d.BrandNames,
            d.DrugClass,
            d.Category,
            d.Indications,
            ContraindicationCount = d.Contraindications.Count,
            BlackBoxWarningCount = d.BlackBoxWarnings.Count,
            InteractionCount = d.Interactions.Count
        });
        return Ok(summary);
    }

    /// <summary>
    /// Search drugs by name, class, or category.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IEnumerable<Drug>), StatusCodes.Status200OK)]
    public ActionResult SearchDrugs([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query 'q' is required.");

        var results = _safetyService.SearchDrugs(q);
        return Ok(results);
    }

    /// <summary>
    /// Get detailed information about a specific drug.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(Drug), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetDrug(string name)
    {
        var drug = _safetyService.GetDrugInfo(name);
        if (drug == null)
            return NotFound($"Drug '{name}' not found in the knowledge base.");

        return Ok(drug);
    }

    /// <summary>
    /// Get contraindications for a specific drug.
    /// </summary>
    [HttpGet("{name}/contraindications")]
    [ProducesResponseType(typeof(IEnumerable<Contraindication>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetContraindications(string name)
    {
        var drug = _safetyService.GetDrugInfo(name);
        if (drug == null)
            return NotFound($"Drug '{name}' not found.");

        return Ok(new
        {
            drug.GenericName,
            drug.Contraindications,
            drug.BlackBoxWarnings,
            drug.Warnings,
            drug.UseWithCaution
        });
    }

    /// <summary>
    /// Get drug interactions for a specific drug.
    /// </summary>
    [HttpGet("{name}/interactions")]
    [ProducesResponseType(typeof(IEnumerable<DrugInteraction>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetInteractions(string name)
    {
        var drug = _safetyService.GetDrugInfo(name);
        if (drug == null)
            return NotFound($"Drug '{name}' not found.");

        return Ok(new
        {
            drug.GenericName,
            drug.Interactions
        });
    }

    /// <summary>
    /// Search for drugs across BOTH the static knowledge base AND OpenFDA.
    /// Use this when a drug is not found locally – it will return FDA label data.
    /// </summary>
    [HttpGet("search/all")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<ActionResult> SearchAllSources([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query 'q' is required.");

        // Search static KB first
        var localResults = _safetyService.SearchDrugs(q)
            .Select(d => new
            {
                d.GenericName,
                d.BrandNames,
                d.DrugClass,
                d.Category,
                d.Indications,
                Source = "MedSafety Knowledge Base"
            }).ToList();

        // Also search OpenFDA
        var fdaResults = await _externalService.SearchFdaDrugsAsync(q);

        return Ok(new
        {
            Query = q,
            StaticKnowledgeBase = new { Count = localResults.Count, Drugs = localResults },
            OpenFDA = new { Count = fdaResults.Count, Drugs = fdaResults },
            TotalFound = localResults.Count + fdaResults.Count
        });
    }

    /// <summary>
    /// Look up a specific drug from OpenFDA when not available in the static knowledge base.
    /// Returns the raw FDA label data including contraindications, warnings, and interactions.
    /// </summary>
    [HttpGet("lookup/{name}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> LookupExternalDrug(string name)
    {
        // Check static KB first
        var localDrug = _safetyService.GetDrugInfo(name);
        if (localDrug != null)
        {
            return Ok(new
            {
                Source = "MedSafety Knowledge Base",
                Drug = localDrug
            });
        }

        // Fall back to OpenFDA
        var fdaLabel = await _externalService.GetFdaLabelAsync(name);
        if (fdaLabel == null)
            return NotFound($"Drug '{name}' not found in the knowledge base or OpenFDA.");

        return Ok(new
        {
            Source = "OpenFDA Drug Label API",
            Drug = new
            {
                GenericName = fdaLabel.OpenFda?.GenericName?.FirstOrDefault() ?? name,
                BrandNames = fdaLabel.OpenFda?.BrandName ?? new(),
                DrugClass = fdaLabel.OpenFda?.PharmClassEpc ?? new(),
                Manufacturer = fdaLabel.OpenFda?.ManufacturerName ?? new(),
                Route = fdaLabel.OpenFda?.Route ?? new(),
                Indications = fdaLabel.IndicationsAndUsage,
                BoxedWarnings = fdaLabel.BoxedWarning,
                Contraindications = fdaLabel.Contraindications,
                Warnings = fdaLabel.Warnings,
                WarningsAndCautions = fdaLabel.WarningsAndCautions,
                DrugInteractions = fdaLabel.DrugInteractions,
                AdverseReactions = fdaLabel.AdverseReactions,
                Pregnancy = fdaLabel.Pregnancy,
                PediatricUse = fdaLabel.PediatricUse,
                GeriatricUse = fdaLabel.GeriatricUse
            }
        });
    }
}
