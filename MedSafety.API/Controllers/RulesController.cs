using MedSafety.API.Models;
using MedSafety.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedSafety.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RulesController : ControllerBase
{
    private readonly CustomSafetyRuleService _rules;

    public RulesController(CustomSafetyRuleService rules)
    {
        _rules = rules;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomSafetyRule>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CustomSafetyRule>> GetRules()
    {
        return Ok(_rules.GetAll());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomSafetyRule), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CustomSafetyRule> GetRule(string id)
    {
        var rule = _rules.GetById(id);
        return rule == null ? NotFound() : Ok(rule);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomSafetyRule), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<CustomSafetyRule> CreateRule([FromBody] UpsertCustomSafetyRuleRequest request)
    {
        if (request == null) return BadRequest(new { errors = new[] { "Rule payload is required." } });
        var errors = CustomSafetyRuleService.Validate(request);
        if (errors.Count > 0) return BadRequest(new { errors });

        var created = _rules.Create(request);
        return CreatedAtAction(nameof(GetRule), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CustomSafetyRule), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CustomSafetyRule> UpdateRule(string id, [FromBody] UpsertCustomSafetyRuleRequest request)
    {
        if (request == null) return BadRequest(new { errors = new[] { "Rule payload is required." } });
        var errors = CustomSafetyRuleService.Validate(request);
        if (errors.Count > 0) return BadRequest(new { errors });

        var updated = _rules.Update(id, request);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteRule(string id)
    {
        return _rules.Delete(id) ? NoContent() : NotFound();
    }
}
