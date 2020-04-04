﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarhammerProfessionApp.Dtos;
using WarhammerProfessionApp.Entities;
using WarhammerProfessionApp.Entities.Models;
using WarhammerProfessionApp.Utility;

namespace WarhammerProfessionApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController, Authorize]
    public class ProfessionsController : ControllerBase
    {
        public ProfessionsController(ProfessionsContext context)
        {
            this.context = context;
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProfession(int id)
        {
            var profession = await context.Professions.FindAsync(id);

            if (profession == null)
                return NotFound();

            context.Set<Profession>().Remove(profession);

            await context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("{id}"), AllowAnonymous]
        public async Task<ActionResult<ProfessionShortenedDetailsDto>> GetProfession(int id)
        {
            var entity = await context.Set<Profession>().Include(a => a.Abilities).ThenInclude(b => b.Abilities).ThenInclude(c => c.Ability)
                .Include(a => a.Skills).ThenInclude(b => b.Skills).ThenInclude(c => c.Skill)
                .Include(a => a.Equipment).ThenInclude(b => b.Items).ThenInclude(c => c.Item)
                .Include(a => a.OutputProfessions).ThenInclude(b => b.OutputProfession)
                .Include(a => a.EntranceProfessions).ThenInclude(b => b.EntranceProfession)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (entity == null)
                return NotFound();

            var profession = new ProfessionShortenedDetailsDto
            {
                Id = entity.Id,
                Name = entity.Name,
                ImageId = entity.ImageId,
                Description = entity.Description ??= string.Join($"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}", Enumerable.Repeat(tempDescription, new Random().Next(1, 4))),
                Agility = entity.Agility,
                Attacks = entity.Attacks,
                CloseCombat = entity.CloseCombat,
                Hitpoints = entity.Hitpoints,
                Inteligence = entity.Inteligence,
                Magic = entity.Magic,
                Polish = entity.Polish,
                Resistance = entity.Resistance,
                Shooting = entity.Shooting,
                Speed = entity.Speed,
                Stamina = entity.Stamina,
                Willpower = entity.Willpower,
                ProfessionLevel = entity.ProfessionLevel.ToString(),
                ProfessionRaceAllowed = entity.ProfessionRaceAllowed.ToString(),
                Abilities = string.Join(", ", entity.Abilities.Select(b => GetFormattedElement(b.Quantity, b.Abilities.Select(c => c.Ability.Name).ToList()))),
                Skills = string.Join(", ", entity.Skills.Select(b => GetFormattedElement(b.Quantity, b.Skills.Select(c => c.Skill.Name).ToList()))),
                Equipment = string.Join(", ", entity.Equipment.Select(b => GetFormattedElement(b.Quantity, b.Items.Select(c => new Tuple<string, int>(c.Item.Name, c.Quantity)).ToList()))),
                EntranceProfessions = entity.EntranceProfessions.Select(b => new ShortProfessionDto
                {
                    Id = b.EntranceProfessionId,
                    Name = b.EntranceProfession.Name
                }).ToList(),
                OutputProfessions = entity.OutputProfessions.Select(b => new ShortProfessionDto
                {
                    Id = b.OutputProfessionId,
                    Name = b.OutputProfession.Name
                }).ToList()
            };

            return profession;
        }

        [HttpGet, AllowAnonymous]
        public async Task<ActionResult<IEnumerable<ProfessionDto>>> GetProfessions()
        {
            var result = await context.Set<Profession>().Select(a => new ProfessionDto
            {
                Id = a.Id,
                Name = a.Name,
                ImageId = a.ImageId,
                Description = a.Description
            }).ToListAsync();

            result.ForEach(a => a.Description ??= string.Join($"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}", Enumerable.Repeat(tempDescription, new Random().Next(1, 4))));

            return result;
        }

        [HttpGet(nameof(GetProfessionsPaths))]
        public async Task<ActionResult<ProfessionPathWrapperDto>> GetProfessionsPaths(int startProfessionId, int endProfessionId, int mappingLevels,
            bool includeStartingProfession, bool includeEndingProfession, byte race)
            => await new ProfessionCostManager(context).GetProfessionsPaths(startProfessionId, endProfessionId, (byte)mappingLevels, includeStartingProfession, includeEndingProfession, race);

        [HttpPost]
        public async Task<ActionResult<ProfessionDto>> PostProfession(ProfessionDto profession)
        {
            var entity = new Profession
            {
                Name = profession.Name,
                Description = profession.Description,
                //Abilities = profession.Abilities.Select(a => new ProfessionAbility { AbilityId = a.Id }).ToList(),
                //Skills = profession.Skills.Select(a => new ProfessionSkill { SkillId = a.Id }).ToList(),
                //Equipment = profession.Equipment.Select(a => new ProfessionItem { ItemId = a.Id, Quality = a.Quality, Quantity = a.Quantity }).ToList(),
                //EntranceProfessions = profession.EntranceProfessions.Select(a => new ProfessionProfession { EntranceProfessionId = a.Id }).ToList(),
                //OutputProfessions = profession.OutputProfessions.Select(a => new ProfessionProfession { OutputProfessionId = a.Id }).ToList(),
            };

            context.Set<Profession>().Add(entity);

            await context.SaveChangesAsync();

            return CreatedAtAction("GetProfession", new { id = profession.Id }, profession);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutProfession(int id, ProfessionDto profession)
        {
            if (id != profession.Id)
                return BadRequest();

            var entity = await context.Set<Profession>().FirstOrDefaultAsync(a => a.Id == id);

            entity.Name = profession.Name;
            entity.Description = profession.Description;
            //entity.Abilities = profession.Abilities.Select(a => new ProfessionAbility { AbilityId = a.Id }).ToList();
            //entity.Skills = profession.Skills.Select(a => new ProfessionSkill { SkillId = a.Id }).ToList();
            //entity.Equipment = profession.Equipment.Select(a => new ProfessionItem { ItemId = a.Id, Quality = a.Quality, Quantity = a.Quantity }).ToList();
            //entity.EntranceProfessions = profession.EntranceProfessions.Select(a => new ProfessionProfession { EntranceProfessionId = a.Id }).ToList();
            //entity.OutputProfessions = profession.OutputProfessions.Select(a => new ProfessionProfession { OutputProfessionId = a.Id }).ToList();

            await context.SaveChangesAsync();

            return NoContent();
        }

        //TODO remove when descriptions will be added in database
        private const string tempDescription = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Pellentesque libero sem, maximus eu rhoncus ac, semper a lectus. " +
                "Etiam rutrum nisl a faucibus feugiat. Aenean quis posuere nisl, volutpat mattis sapien. Maecenas non condimentum nulla. " +
                "Etiam viverra justo sit amet erat lobortis, non viverra nisi molestie. Etiam sagittis vel mauris vel tincidunt. " +
                "Pellentesque vitae arcu gravida, tristique purus sit amet, semper justo. Aliquam ac nunc sed orci rutrum fringilla quis eu diam. ";

        private readonly ProfessionsContext context;

        private string GetFormattedElement(int quantity, IEnumerable<Tuple<string, int>> values)
        {
            var convertedValues = values.Select(a => a.Item2 > 1 ? $"{a.Item2} x {a.Item1}" : $"{a.Item1}");

            if (quantity < 2)
                return string.Join(" albo ", convertedValues);
            else
                return $"Wybierz {quantity} z {string.Join(", ", convertedValues)}";
        }

        private string GetFormattedElement(int quantity, IEnumerable<string> values)
        {
            if (quantity < 2)
                return string.Join(" albo ", values);
            else
                return $"Wybierz {quantity} z {string.Join(", ", values)}";
        }
    }
}