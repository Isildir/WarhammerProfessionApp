﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using WarhammerApp.Commons.Dtos;
using WarhammerApp.API.Entities;
using WarhammerApp.API.Entities.Models;
using WarhammerApp.API.Utility;

namespace WarhammerApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController, Authorize]
    public class ItemsController : ControllerBase
    {
        private readonly ProfessionsContext context;

        public ItemsController(ProfessionsContext context)
        {
            this.context = context;
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var ability = await context.Set<Ability>().FindAsync(id);

            if (ability == null)
                return NotFound();

            context.Set<Ability>().Remove(ability);

            await context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ItemDto>> GetItem(int id)
        {
            var item = await context.Set<Item>().FindAsync(id);

            if (item == null)
                return NotFound();

            var money = MoneyCalculator.ConvertMoney(item.Price);

            return new ItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description,
                ItemType = item.ItemType.ToString(),
                Gold = money.Gold,
                Silver = money.Silver,
                Bronze = money.Bronze,
                Rarity = item.Rarity.ToString(),
                Weight = item.Weight
            };
        }

        [HttpGet, AllowAnonymous]
        public async Task<ActionResult<IEnumerable<ItemDto>>> GetItems()
        {
            var entities = await context.Set<Item>().ToListAsync();

            var result = new List<ItemDto>();

            foreach (var item in entities)
            {
                var money = MoneyCalculator.ConvertMoney(item.Price);

                var record = new ItemDto
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    ItemType = item.ItemType.ToString(),
                    Gold = money.Gold,
                    Silver = money.Silver,
                    Bronze = money.Bronze,
                    Rarity = item.Rarity.ToString(),
                    Weight = item.Weight
                };

                result.Add(record);
            }

            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<Item>> PostItem(ItemDto item)
        {
            if (item == null)
                return BadRequest();

            var entity = new Ability { Name = item.Name };

            context.Set<Ability>().Add(entity);

            await context.SaveChangesAsync();

            return CreatedAtAction("GetAbility", new { id = item.Id }, item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutItem(int id, ItemDto item)
        {
            if (id != item.Id)
                return BadRequest();

            var entity = await context.Set<Ability>().FindAsync(id);

            if (entity == null)
                return NotFound();

            entity.Name = item.Name;

            await context.SaveChangesAsync();

            return NoContent();
        }
    }
}