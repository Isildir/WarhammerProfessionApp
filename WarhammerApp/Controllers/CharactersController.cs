﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarhammerProfession.Commons.Utility;
using WarhammerApp.Commons.Dtos;
using WarhammerApp.API.Entities;
using WarhammerApp.API.Entities.Models;
using WarhammerApp.API.Entities.Models.Enums;
using WarhammerApp.API.SignalR;
using WarhammerApp.API.Utility;

namespace WarhammerApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController, Authorize]
    public class CharactersController : ControllerBase
    {
        private readonly CharacterHub characterHub;
        private readonly ProfessionsContext context;

        public CharactersController(ProfessionsContext context, CharacterHub characterHub)
        {
            this.context = context;
            this.characterHub = characterHub;
        }

        public static Dictionary<int, int> ActiveUsersProfessions { get; set; }

        [HttpGet]
        public async Task<ActionResult<CharacterDto>> GetCharacter()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = await context.Characters
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Skills).ThenInclude(a => a.Skill)
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .Include(a => a.Items).ThenInclude(a => a.Item)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.AdditionalItems)
                .Include(a => a.AdditionalValues)
                .Include(a => a.CurrentProfession)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var money = MoneyCalculator.ConvertMoney(character.Money);
            var load = character.Items.Any() ? character.Items.Sum(a => a.Quantity * a.Item.Weight) : 0;
            var maxLoad = character.Statistics.FirstOrDefault(a => a.Statistic.Type == StatisticType.Stamina).CurrentValue * 10;

            var basicValues = new CharacterBasicValuesDto
            {
                Id = character.Id,
                ActualProfessionName = character.CurrentProfession?.Name,
                ExperienceLeft = character.ExperienceSummary - character.ExperienceUsed,
                ExperienceSum = character.ExperienceSummary,
                Name = character.Name,
                Notes = character.Notes,
                CurrentLoad = $"{load} / {maxLoad}",
                Race = character.Race != null ? new RaceDto
                {
                    Id = (int)character.Race,
                    Name = character.Race.ToString()
                } : null
            };

            var professions = character.Professions.Select(a => new ShortProfessionDto { Id = a.Profession.Id, Name = a.Profession.Name }).ToList();

            var additionalItems = character.AdditionalItems.Select(a => new CharacterAdditionalItemDto
            {
                Id = a.Id,
                Name = a.Name,
                Weight = a.Weight,
                Quantity = a.Quantity,
                Description = a.Description
            }).ToList(); ;

            var additionalValues = character.AdditionalValues.Select(a => new AdditionalCharacterValueDto
            {
                Id = a.Id,
                Name = a.Name
            }).ToList();

            var skills = character.Skills.Select(a => new CharacterSkillDto
            {
                Id = a.SkillId,
                Name = a.Skill.Name,
                Trait = EnumTranslator.TranslateStaticticValue(a.Skill.Trait),
                Description = a.Skill.Description,
                //Level = a.Level
            }).ToList();

            var abilities = character.Abilities.Select(a => new AbilityDto
            {
                Id = a.AbilityId,
                Name = a.Ability.Name,
                Description = a.Ability.Description
            }).ToList();

            var items = character.Items.Select(a =>
            {
                var convertedPrice = MoneyCalculator.ConvertMoney(a.Item.Price);

                return new CharacterItemDto
                {
                    Id = a.Item.Id,
                    Name = a.Item.Name,
                    Weight = a.Item.Weight,
                    Description = a.Item.Description,
                    Quantity = a.Quantity,
                    Gold = convertedPrice.Gold,
                    Silver = convertedPrice.Silver,
                    Bronze = convertedPrice.Bronze
                };
            }).ToList();

            var statistics = new List<Tuple<bool, CharacterStatisticDto>>();

            foreach (var value in character.Statistics)
            {
                var maximumValue = GetTotalStatisticMaximumValue(value.Statistic.Type, character, out int professionValue, out Dictionary<string, int> abilitiesValues);
                var staticValues = abilitiesValues.Any() ? abilitiesValues.Sum(a => a.Value) : 0;

                var result = new CharacterStatisticDto
                {
                    Type = value.Statistic.Type,
                    Name = EnumTranslator.TranslateStaticticValue(value.Statistic.Type),
                    IsReadOnly = value.Statistic.IsReadOnly,
                    BaseValue = value.BaseValue,
                    CurrentValue = value.CurrentValue,
                    MaximumValue = maximumValue,
                    Details = $"Bazowe {value.BaseValue}",
                };

                if (value.Statistic.Type == StatisticType.Hardness)
                {
                    var val = character.Statistics.FirstOrDefault(a => a.Statistic.Type == StatisticType.Resistance).CurrentValue / 10;

                    result.BaseValue = val;
                    result.CurrentValue = val;
                    result.MaximumValue = val;
                }
                else if (value.Statistic.Type == StatisticType.Strength)
                {
                    var val = character.Statistics.FirstOrDefault(a => a.Statistic.Type == StatisticType.Stamina).CurrentValue / 10;

                    result.BaseValue = val;
                    result.CurrentValue = val;
                    result.MaximumValue = val;
                }

                if (value.Statistic.IsCalculatedValue)
                {
                    result.CanBeDecreased = false;
                    result.CanBeIncreased = false;
                }
                else
                {
                    result.CanBeDecreased = value.CurrentValue > value.BaseValue + staticValues;
                    result.CanBeIncreased = value.CurrentValue < maximumValue;
                }

                if (character.Professions.Any() && professionValue != 0)
                    result.Details += $" + {professionValue} z rozwoju";

                if (abilitiesValues.Any())
                    result.Details += $" + {string.Join(',', abilitiesValues.Select(a => $"{a.Value} z {a.Key}"))}";

                if (value.Statistic.Type == StatisticType.Speed && (load - maxLoad) / 50 > 1)
                {
                    var overload = (load - maxLoad) / 50;

                    result.CurrentValue -= overload;
                    result.Details += $" - {overload} z przeciążenia";
                }

                statistics.Add(new Tuple<bool, CharacterStatisticDto>(value.Statistic.IsBasicValue, result));
            }

            return Ok(new CharacterDto
            {
                Money = money,
                Items = items,
                Skills = skills,
                Abilities = abilities,
                BasicValues = basicValues,
                Professions = professions,
                AdditionalItems = additionalItems,
                AdditionalValues = additionalValues,
                BasicStatistics = statistics.Where(a => a.Item1).Select(a => a.Item2).ToList(),
                AdvancedStatistics = statistics.Where(a => !a.Item1).Select(a => a.Item2).ToList()
            });
        }

        [HttpGet(nameof(GetUserCharacters))]
        public ActionResult<List<ShortCharacterDto>> GetUserCharacters()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var result = context.Characters
                .Include(a => a.CurrentProfession)
                .Where(a => a.UserId == userId)
                .Select(a => new ShortCharacterDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Race = a.Race.ToString(),
                    ProfessionName = a.CurrentProfession.Name
                }).ToList();

            return Ok(result);
        }

        [HttpPost(nameof(SetActiveCharacter))]
        public ActionResult SetActiveCharacter(int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            if (!context.Characters.Any(a => a.Id == id && a.UserId == userId))
                return BadRequest();

            if (ActiveUsersProfessions.ContainsKey(userId))
                ActiveUsersProfessions[userId] = id;
            else
                ActiveUsersProfessions.Add(userId, id);

            return Ok();
        }

        #region Others

        [HttpPost(nameof(ChangeBaseStatisticValue))]
        public ActionResult<CharacterChangeResponseDto> ChangeBaseStatisticValue([FromBody] BaseStatiticValueChangeDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var characterStatistic = character.Statistics.FirstOrDefault(a => a.Statistic.Type == value.Type);

            characterStatistic.CurrentValue -= characterStatistic.BaseValue;
            characterStatistic.BaseValue = value.Value;
            characterStatistic.CurrentValue += characterStatistic.BaseValue;

            context.SaveChanges();

            SendMessageAboutStatisticValueChange(character, characterStatistic);

            return Ok();
        }

        [HttpPost(nameof(ChangeMaximumExperience))]
        public ActionResult<CharacterChangeResponseDto> ChangeMaximumExperience([FromBody] int value)
        {
            var character = GetUserCharacter();

            if (character == null)
                return NotFound();

            character.ExperienceSummary = value;

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            return Ok(false);
        }

        [HttpPost(nameof(ChangeMoney))]
        public ActionResult<CharacterChangeResponseDto> ChangeMoney([FromBody] MoneyDto value)
        {
            var character = GetUserCharacter();

            if (character == null)
                return NotFound();

            character.Money = MoneyCalculator.GetUserMoney(value.Gold, value.Silver, value.Bronze);

            context.SaveChanges();

            return Ok(false);
        }

        [HttpPost(nameof(ChangeName))]
        public ActionResult<CharacterChangeResponseDto> ChangeName([FromBody] string name)
        {
            var character = GetUserCharacter();

            if (character == null)
                return NotFound();

            character.Name = name;

            context.SaveChanges();

            return Ok(false);
        }

        [HttpPost(nameof(ChangeNotes))]
        public ActionResult<bool> ChangeNotes([FromBody] string value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            character.Notes = value;

            context.SaveChanges();

            return Ok();
        }

        [HttpPost(nameof(ChangeStatisticValue))]
        public ActionResult<CharacterChangeResponseDto> ChangeStatisticValue([FromBody] StatiticValueChangeDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var characterStatistic = character.Statistics.FirstOrDefault(a => a.Statistic.Type == value.Type);

            if (characterStatistic.Statistic.IsCalculatedValue)
                return BadRequest();

            var incrementValue = characterStatistic.Statistic.IsBasicValue ? 5 : 1;

            var maximumValue = GetTotalStatisticMaximumValue(characterStatistic.Statistic.Type, character, out int professionValue, out Dictionary<string, int> _);

            if (value.IncrementingValue)
            {
                if (!CheckCharacterExperienceLimit(character, 100) || characterStatistic.CurrentValue + incrementValue > maximumValue)
                    return BadRequest("Za mało punktów doświadczenia lub przekroczono limit");

                character.ExperienceUsed += 100;
                characterStatistic.CurrentValue += incrementValue;
            }
            else
            {
                if (characterStatistic.CurrentValue - incrementValue < maximumValue - professionValue)
                    return BadRequest("Przekroczono dolną granicę");

                character.ExperienceUsed -= 100;
                characterStatistic.CurrentValue -= incrementValue;
            }

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            SendMessageAboutStatisticValueChange(character, characterStatistic);

            return Ok(characterStatistic.Statistic.Type == StatisticType.Stamina || characterStatistic.Statistic.Type == StatisticType.Resistance);
        }

        [HttpGet(nameof(GetRaces))]
        public ActionResult<RaceDto> GetRaces()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .FirstOrDefault(a => a.UserId == userId);

            var values = SortAvailableRaces(character);

            return Ok(values);
        }

        [HttpPost(nameof(SetRace))]
        public ActionResult<CharacterChangeResponseDto> SetRace([FromBody] int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .FirstOrDefault(a => a.UserId == userId);

            var values = SortAvailableRaces(character);

            if (!values.Any(a => a.Id == id))
                return BadRequest();

            character.Race = (Race)id;

            context.SaveChanges();

            return Ok();
        }

        #endregion Others

        #region Professions

        [HttpGet(nameof(GetFilteredProfessions))]
        public ActionResult<ShortProfessionDto> GetFilteredProfessions()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Skills).ThenInclude(a => a.Skills)
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Abilities).ThenInclude(a => a.Abilities)
                .FirstOrDefault(a => a.UserId == userId);

            //if (!CheckIfProfessionsAreFinished(character))
            //   return Ok();

            var characterProfessions = character.Professions.Select(a => a.ProfessionId).ToList();

            var values = context.Professions.Where(a => !characterProfessions.Contains(a.Id));

            if (character.CurrentProfessionId.HasValue)
                values = values.Where(a => a.EntranceProfessions.Any(b => b.EntranceProfessionId == character.CurrentProfessionId.Value));
            else
                values = values.Where(a => a.IsBasicLevel);

            return Ok(values.Select(a => new ShortProfessionDto { Id = a.Id, Name = a.Name }).ToList());
        }

        [HttpPost(nameof(RemoveLastProfession))]
        public ActionResult<int?> RemoveLastProfession()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!character.Professions.Any())
                return BadRequest();

            var maxProfessionOrder = character.Professions.Max(a => a.Order);
            var professionToRemove = character.Professions.First(a => a.Order == maxProfessionOrder);
            var newCurrentProfession = character.Professions.FirstOrDefault(a => a.Order == maxProfessionOrder - 1);

            character.Professions.Remove(professionToRemove);

            if (character.CurrentProfessionId.HasValue && character.CurrentProfessionId.Value == professionToRemove.ProfessionId)
            {
                var targetProfession = context.Professions
                    .Include(a => a.Skills)
                    .Include(a => a.Abilities)
                    .FirstOrDefault(a => a.Id == professionToRemove.ProfessionId);

                character.ExperienceSummary -= (targetProfession.Skills.Count + targetProfession.Abilities.Count) * 100;
            }

            character.CurrentProfessionId = newCurrentProfession?.ProfessionId;

            if (maxProfessionOrder > 1)
                character.ExperienceUsed -= 100;

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);
            SendMessageAboutExperienceSummaryChange(character);
            character.Statistics.ForEach(a => SendMessageAboutStatisticValueChange(character, a));

            return Ok(newCurrentProfession?.ProfessionId ?? 0);
        }

        [HttpPost(nameof(SetNextProfession))]
        public ActionResult<CharacterChangeResponseDto> SetNextProfession([FromBody] int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var targetProfession = context.Professions
                .Include(a => a.EntranceProfessions)
                .Include(a => a.Skills)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .FirstOrDefault(a => a.Id == id);

            if (targetProfession == null || character.Professions.Any(a => a.ProfessionId == id))
                return BadRequest();

            if (character.CurrentProfessionId.HasValue && !targetProfession.EntranceProfessions.Any(a => a.EntranceProfessionId == character.CurrentProfessionId.Value))
                return BadRequest();

            var maxProfessionOrder = character.Professions.Any() ? character.Professions.Max(a => a.Order) : (byte)0;

            if (character.CurrentProfessionId.HasValue)
                character.ExperienceUsed += 100;
            else
                character.ExperienceSummary += (targetProfession.Skills.Count + targetProfession.Abilities.Count) * 100;

            character.CurrentProfessionId = id;
            character.Professions.Add(new CharacterProfession
            {
                Profession = targetProfession,
                Order = ++maxProfessionOrder
            });

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);
            SendMessageAboutExperienceSummaryChange(character);
            character.Statistics.ForEach(a => SendMessageAboutStatisticValueChange(character, a));

            return Ok(true);
        }

        #endregion Professions

        #region Items

        [HttpPost(nameof(AddItem))]
        public ActionResult<CharacterItemDto> AddItem(ModifyCharacterItemDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.Items).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!context.Items.Any(a => a.Id == value.Id))
                return BadRequest();

            var item = context.Items.FirstOrDefault(a => a.Id == value.Id);

            if (value.ChangeMoney)
            {
                if (character.Money < item.Price)
                {
                    SendMessage(character.Id, "Nie wystarczająco pieniędzy");

                    return BadRequest();
                }

                character.Money -= item.Price;
            }

            character.Items.Add(new CharacterItem
            {
                ItemId = value.Id,
                Quantity = 1
            });

            context.SaveChanges();

            var convertedPrice = MoneyCalculator.ConvertMoney(item.Price);

            var result = new CharacterItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Quantity = 1,
                Weight = item.Weight,
                Description = item.Description,
                Gold = convertedPrice.Gold,
                Silver = convertedPrice.Silver,
                Bronze = convertedPrice.Bronze
            };

            if (value.ChangeMoney)
                SendMessageAboutMoneyChange(character);

            return Ok(result);
        }

        [HttpGet(nameof(GetFilteredItems))]
        public ActionResult<CharacterItemDto> GetFilteredItems(string filter)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Items)
                .FirstOrDefault(a => a.UserId == userId);

            var takenItemsIds = character.Items.Select(a => a.ItemId).ToList();

            var query = context.Items.Where(a => !takenItemsIds.Contains(a.Id));

            if (!string.IsNullOrEmpty(filter))
                query = query.Where(a => a.Name.Contains(filter));

            var values = query.Take(10).Select(a => new
            {
                a.Id,
                a.Name,
                a.Price,
                a.Weight
            }).ToList();

            var convertedValues = new List<CharacterItemDto>();

            foreach (var item in values)
            {
                var money = MoneyCalculator.ConvertMoney(item.Price);

                var value = new CharacterItemDto
                {
                    Id = item.Id,
                    Name = item.Name,
                    Weight = item.Weight,
                    Gold = money.Gold,
                    Silver = money.Silver,
                    Bronze = money.Bronze
                };

                convertedValues.Add(value);
            }

            return Ok(convertedValues);
        }

        [HttpPost(nameof(ModifyItem))]
        public ActionResult<int> ModifyItem(ModifyCharacterItemDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Items)
                .ThenInclude(a => a.Item)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!context.Items.Any(a => a.Id == value.Id))
                return BadRequest();

            var item = character.Items.FirstOrDefault(a => a.ItemId == value.Id);

            if (value.ChangeMoney)
            {
                var change = value.Quantity - item.Quantity;

                if (change != 0)
                {
                    if (change > 0 && character.Money < item.Item.Price * change)
                    {
                        SendMessage(character.Id, "Nie wystarczająco pieniędzy");

                        return BadRequest();
                    }

                    character.Money -= item.Item.Price * change;
                }
            }

            item.Quantity = value.Quantity;

            context.SaveChanges();

            if (value.ChangeMoney)
                SendMessageAboutMoneyChange(character);

            return Ok();
        }

        [HttpPost(nameof(RemoveItem))]
        public ActionResult<int> RemoveItem([FromBody] ModifyCharacterItemDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Items)
                .ThenInclude(a => a.Item)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!context.Items.Any(a => a.Id == value.Id))
                return BadRequest();

            var item = character.Items.FirstOrDefault(a => a.ItemId == value.Id);

            if (value.ChangeMoney)
                character.Money += item.Item.Price * item.Quantity;

            character.Items.Remove(item);

            context.SaveChanges();

            if (value.ChangeMoney)
                SendMessageAboutMoneyChange(character);

            return Ok();
        }

        #endregion Items

        #region Abilities

        [HttpPost(nameof(AddAbility))]
        public ActionResult<AbilityDto> AddAbility([FromBody] int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!CheckCharacterExperienceLimit(character, 100))
                return BadRequest();

            var ability = context.Abilities.FirstOrDefault(a => a.Id == id);

            if (ability == null || character.Abilities.Any(a => a.AbilityId == id))
                return BadRequest();

            character.ExperienceUsed += 100;
            character.Abilities.Add(new CharacterAbility { AbilityId = id });

            CharacterStatistic characterStatistic = null;

            if (ability.HasImpactOnStatictics)
            {
                characterStatistic = character.Statistics.FirstOrDefault(a => a.Statistic.Type == ability.ValueToAlter.Value);

                characterStatistic.CurrentValue += ability.ImpactValue.Value;
            }

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            if (ability.HasImpactOnStatictics)
                SendMessageAboutStatisticValueChange(character, characterStatistic);

            var result = new AbilityDto
            {
                Id = ability.Id,
                Name = ability.Name,
                Description = ability.Description
            };

            return Ok(result);
        }

        [HttpGet(nameof(GetFilteredAbilities))]
        public ActionResult<AbilityDto> GetFilteredAbilities()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Abilities)
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .ThenInclude(a => a.Abilities)
                .ThenInclude(a => a.Abilities)
                .ThenInclude(a => a.Ability)
                .FirstOrDefault(a => a.UserId == userId);

            var availableAbilities = character.Professions.SelectMany(a => a.Profession.Abilities.SelectMany(b => b.Abilities.Select(c => c.Ability))).ToList();
            var availableAbilitiesIds = availableAbilities.Select(c => c.Id).Distinct().ToList();
            var takenAbilitiesIds = character.Abilities.Select(a => a.AbilityId).ToList();

            var filteredAbilitiesIds = availableAbilitiesIds.Where(a => !takenAbilitiesIds.Contains(a)).ToList();

            var values = filteredAbilitiesIds.Select(a => availableAbilities.FirstOrDefault(b => b.Id == a)).Select(a => new AbilityDto
            {
                Id = a.Id,
                Name = a.Name
            }).ToList();

            return Ok(values);
        }

        [HttpDelete(nameof(RemoveAbility))]
        public ActionResult<CharacterChangeResponseDto> RemoveAbility(int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Abilities).ThenInclude(a => a.Ability)
                .Include(a => a.Statistics).ThenInclude(a => a.Statistic)
                .Include(a => a.Professions).ThenInclude(a => a.Profession).ThenInclude(a => a.Statistics).ThenInclude(a => a.Statistic)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var ability = context.Abilities.FirstOrDefault(a => a.Id == id);

            var characterAbility = character.Abilities.FirstOrDefault(a => a.AbilityId == id);

            if (ability == null || characterAbility == null)
                return BadRequest();

            character.ExperienceUsed -= 100;
            character.Abilities.Remove(characterAbility);

            CharacterStatistic characterStatistic = null;

            if (ability.HasImpactOnStatictics)
            {
                characterStatistic = character.Statistics.FirstOrDefault(a => a.Statistic.Type == ability.ValueToAlter.Value);

                characterStatistic.CurrentValue -= ability.ImpactValue.Value;
            }

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            if (ability.HasImpactOnStatictics)
                SendMessageAboutStatisticValueChange(character, characterStatistic);

            return Ok(ability.HasImpactOnStatictics);
        }

        #endregion Abilities

        #region Skills

        [HttpPost(nameof(AddSkill))]
        public ActionResult<CharacterSkillDto> AddSkill([FromBody] CharacterSkillAddDto model)
        {
            if (model == null)
                return BadRequest();

            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.AllowedValues)
                .Include(a => a.Skills)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var skillAvailabilityCondition = character.Professions
                .SelectMany(a => a.Profession.Skills)
                .Any(a => a.Id == model.WrapperId && a.Skills
                .Any(b => b.SkillId == model.Id && (b.AllowAllValues ?? false || b.AllowedValues
                .Any(c => c.DictionaryValueId == model.DictionaryValueId))));

            if (!skillAvailabilityCondition || character.Skills.Any(a => a.ProfessionSkillsId == model.WrapperId))
                return BadRequest();

            if (!CheckCharacterExperienceLimit(character, 100))
            {
                SendMessage(character.Id, "Za mało doświadczenia");

                return BadRequest();
            }

            //var characterSkill = character.Skills.FirstOrDefault(a => a.SkillId == id);
            /*
            if (!context.Skills.Any(a => a.Id == id) || (characterSkill != null && characterSkill.Level >= 3))
                return BadRequest();
                */
            character.ExperienceUsed += 100;
            /*
            if (characterSkill == null)
                character.Skills.Add(new CharacterSkill { SkillId = id, Level = 1 });
            else
                characterSkill.Level++;
                */
            context.SaveChanges();

            //var dbSkill = context.Skills.First(a => a.Id == id);

            SendMessageAboutExperienceChange(character);

            var skill = new CharacterSkillDto
            {
                //Id = dbSkill.Id,
                //Name = dbSkill.Name,
                //Trait = dbSkill.Trait.ToString(),
                //Level = characterSkill.Level,
                //Description = dbSkill.Description
            };

            return Ok(skill);
        }

        [HttpGet(nameof(GetFilteredSkills))]
        public ActionResult<CharacterSkillGetDto> GetFilteredSkills()
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.Skills)
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.AllowedValues)
                .Include(a => a.Professions)
                .ThenInclude(a => a.Profession)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.Skills)
                .ThenInclude(a => a.Skill)
                .ThenInclude(a => a.Dictionary)
                .ThenInclude(a => a.Values)
                .FirstOrDefault(a => a.UserId == userId);

            var options = character.Professions.SelectMany(a => a.Profession.Skills.SelectMany(b => b.Skills.Select(c => new
            {
                ProfessionName = a.Profession.Name,
                WrapperId = c.Id,
                c.SkillId,
                c.Skill.Dictionary,
                SkillName = c.Skill.Name,
                c.Skill.Trait,
                c.AllowAllValues,
                AllowedValues = c.AllowedValues.Select(d => new
                {
                    d.DictionaryValueId,
                    d.DictionaryValue.Value
                })
            }))).ToList();

            var usedValues = character.Skills.Select(a => new
            {
                a.SkillId,
                a.DictionaryValueId,
                a.ProfessionSkillsId
            }).ToList();

            var filteredOptions = options.Where(a => !usedValues.Any(b => b.ProfessionSkillsId == a.WrapperId)).ToList();

            var convertedOptions = new List<CharacterSkillGetDto>();

            foreach (var option in filteredOptions)
            {
                if (option.Dictionary == null)
                    convertedOptions.Add(new CharacterSkillGetDto
                    {
                        Id = option.SkillId,
                        Name = $"{option.ProfessionName}: {option.SkillName}",
                        WrapperId = option.WrapperId,
                        Trait = option.Trait.ToString(),
                        Level = usedValues.Count(a => a.SkillId == option.SkillId) + 1
                    });
                else
                {
                    if (option.AllowAllValues.HasValue && option.AllowAllValues.Value)
                        foreach (var value in option.Dictionary.Values)
                            convertedOptions.Add(new CharacterSkillGetDto
                            {
                                Id = option.SkillId,
                                Name = $"{option.ProfessionName}: {option.SkillName} ({value.Value})",
                                WrapperId = option.WrapperId,
                                Trait = option.Trait.ToString(),
                                Level = usedValues.Count(a => a.SkillId == option.SkillId && a.DictionaryValueId == value.Id) + 1,
                                DictionaryValueId = value.Id
                            });
                    else
                        foreach (var value in option.AllowedValues)
                            convertedOptions.Add(new CharacterSkillGetDto
                            {
                                Id = option.SkillId,
                                Name = $"{option.ProfessionName}: {option.SkillName} ({value.Value})",
                                WrapperId = option.WrapperId,
                                Trait = option.Trait.ToString(),
                                Level = usedValues.Count(a => a.SkillId == option.SkillId && a.DictionaryValueId == value.DictionaryValueId) + 1,
                                DictionaryValueId = value.DictionaryValueId
                            });
                }
            }

            convertedOptions.RemoveAll(a => a.Level > 3);

            return Ok(convertedOptions);
        }

        [HttpDelete(nameof(RemoveSkill))]
        public ActionResult<CharacterChangeResponseDto> RemoveSkill(int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.Skills).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var skill = character.Skills.FirstOrDefault(a => a.SkillId == id);

            if (skill == null)
                return BadRequest();

            character.ExperienceUsed -= 100;
            /*
            if (skill.Level <= 1)
                character.Skills.Remove(skill);
            else
                skill.Level--;
                */
            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            return Ok(false);
        }

        #endregion Skills

        #region AdditionalItems

        [HttpPost(nameof(AddAdditionalItem))]
        public ActionResult<CharacterAdditionalItemDto> AddAdditionalItem([FromBody] CharacterAdditionalItemDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.AdditionalValues).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var model = new AdditionalCharacterItem
            {
                Name = value.Name,
                Weight = value.Weight,
                Quantity = value.Quantity,
                Description = value.Description
            };

            character.AdditionalItems.Add(model);

            context.SaveChanges();

            value.Id = model.Id;

            return Ok(value);
        }

        [HttpPost(nameof(ModifyAdditionalItem))]
        public ActionResult<CharacterChangeResponseDto> ModifyAdditionalItem([FromBody] CharacterAdditionalItemDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.AdditionalItems).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var characterValue = character.AdditionalItems.FirstOrDefault(a => a.Id == value.Id);

            if (characterValue == null)
                return BadRequest();

            characterValue.Name = value.Name;
            characterValue.Weight = value.Weight;
            characterValue.Quantity = value.Quantity;
            characterValue.Description = value.Description;

            context.SaveChanges();

            return Ok();
        }

        [HttpDelete(nameof(RemoveAdditionalItem))]
        public ActionResult<CharacterChangeResponseDto> RemoveAdditionalItem(int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters
                .Include(a => a.AdditionalItems)
                .FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            var value = character.AdditionalItems.FirstOrDefault(a => a.Id == id);

            if (value == null)
                return BadRequest();

            character.AdditionalItems.Remove(value);

            context.SaveChanges();

            return Ok();
        }

        #endregion AdditionalItems

        #region AdditionalValues

        [HttpPost(nameof(AddAdditionalValue))]
        public ActionResult<CharacterChangeResponseDto> AddAdditionalValue([FromBody] AdditionalCharacterValueDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.AdditionalValues).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!CheckCharacterExperienceLimit(character, 100))
                return BadRequest();

            character.ExperienceUsed += 100;
            character.AdditionalValues.Add(new AdditionalCharacterValue { Name = value.Name });

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            return Ok(false);
        }

        [HttpPost(nameof(ModifyAdditionalValue))]
        public ActionResult<CharacterChangeResponseDto> ModifyAdditionalValue([FromBody] AdditionalCharacterValueDto value)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.AdditionalValues).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!character.AdditionalValues.Any(a => a.Id == value.Id))
                return BadRequest();

            character.AdditionalValues.FirstOrDefault(a => a.Id == value.Id).Name = value.Name;

            context.SaveChanges();

            return Ok(false);
        }

        [HttpDelete(nameof(RemoveAdditionalValue))]
        public ActionResult<CharacterChangeResponseDto> RemoveAdditionalValue(int id)
        {
            var userId = ClaimsReader.GetUserId(Request);

            var character = context.Characters.Include(a => a.AdditionalValues).FirstOrDefault(a => a.UserId == userId);

            if (character == null)
                return NotFound();

            if (!character.AdditionalValues.Any(a => a.Id == id))
                return BadRequest();

            character.ExperienceUsed -= 100;
            character.AdditionalValues.Remove(character.AdditionalValues.First(a => a.Id == id));

            context.SaveChanges();

            SendMessageAboutExperienceChange(character);

            return Ok(false);
        }

        #endregion AdditionalValues

        #region PrivateMethods

        private bool CheckCharacterExperienceLimit(Character character, int value) => character.ExperienceSummary - character.ExperienceUsed >= value;

        /*
        private bool CheckIfProfessionsAreFinished(Character character)
        {
            var allCharacterSkillsIds = character.Skills.Select(a => a.SkillId).ToList();
            var allCharacterAbilitiesIds = character.Abilities.Select(a => a.AbilityId).ToList();

            var skillsCondition = character.Professions.SelectMany(a => a.Profession.Skills).All(a => a.Skills.Count(b => allCharacterSkillsIds.Contains(b.SkillId)) >= a.Quantity);
            var abilityCondition = character.Professions.SelectMany(a => a.Profession.Abilities).All(a => a.Abilities.Count(b => allCharacterAbilitiesIds.Contains(b.AbilityId)) >= a.Quantity);
            var statisticsCondition = true;

            foreach (var value in character.Statistics)
            {
                var maximumValue = GetTotalStatisticMaximumValue(value.Statistic.Type, character, out int professionValue, out Dictionary<string, int> abilitiesValues);

                if (value.CurrentValue < maximumValue)
                {
                    statisticsCondition = false;
                    break;
                }
            }

            return skillsCondition && abilityCondition && statisticsCondition;
        }
        */

        private int GetBonusStatisticMaximumValue(StatisticType type, Character character)
        {
            var bonuses = character.Abilities
                .Where(b => b.Ability.HasImpactOnStatictics && b.Ability.ValueToAlter.Value == type)
                .ToDictionary(a => a.Ability.Name, a => a.Ability.ImpactValue.Value);

            return bonuses.Any() ? bonuses.Sum(a => a.Value) : 0;
        }

        private int GetProfessionStatisticMaximumValue(StatisticType type, Character character)
            => character.Professions.Max(a => a.Profession.Statistics.FirstOrDefault(b => b.Statistic.Type == type)?.Value ?? 0);

        private int GetTotalStatisticMaximumValue(StatisticType type, Character character, out int bonusesValue)
        {
            var maximumValueFromProfessions = character.Professions.Any() ? GetProfessionStatisticMaximumValue(type, character) : 0;
            bonusesValue = GetBonusStatisticMaximumValue(type, character);

            return character.Statistics.First(a => a.Statistic.Type == type).BaseValue + bonusesValue + maximumValueFromProfessions;
        }

        private int GetTotalStatisticMaximumValue(StatisticType type, Character character, out int professionValue, out Dictionary<string, int> abilitiesValues)
        {
            var maximumValueFromProfessions = 0;
            var bonuses = character.Abilities
                .Where(b => b.Ability.HasImpactOnStatictics && b.Ability.ValueToAlter.Value == type)
                .ToDictionary(a => a.Ability.Name, a => a.Ability.ImpactValue.Value);

            if (character.Professions.Any())
                maximumValueFromProfessions = GetProfessionStatisticMaximumValue(type, character);

            professionValue = maximumValueFromProfessions;
            abilitiesValues = bonuses;

            return character.Statistics.First(a => a.Statistic.Type == type).BaseValue + (bonuses.Any() ? bonuses.Sum(a => a.Value) : 0) + maximumValueFromProfessions;
        }

        private Character GetUserCharacter()
        {
            var userId = ClaimsReader.GetUserId(Request); ClaimsReader.GetUserId(Request);

            return context.Characters.FirstOrDefault(a => a.UserId == userId);
        }

        private List<RaceDto> SortAvailableRaces(Character character)
        {
            var allowedProfessions = character.Professions.Select(a => a.Profession.ProfessionRaceAllowed).ToList();
            var mostRestrictedValue = (Race)(allowedProfessions.Any() ? allowedProfessions.Min(a => (int)a) : (int)(Race.Human | Race.Elf | Race.Dwarf | Race.Halfing));

            var values = new List<RaceDto>();

            if (mostRestrictedValue.HasFlag(Race.Human))
                values.Add(new RaceDto
                {
                    Id = (int)Race.Human,
                    Name = Race.Human.ToString()
                });

            if (mostRestrictedValue.HasFlag(Race.Elf))
                values.Add(new RaceDto
                {
                    Id = (int)Race.Elf,
                    Name = Race.Elf.ToString()
                });

            if (mostRestrictedValue.HasFlag(Race.Dwarf))
                values.Add(new RaceDto
                {
                    Id = (int)Race.Dwarf,
                    Name = Race.Dwarf.ToString()
                });

            if (mostRestrictedValue.HasFlag(Race.Halfing))
                values.Add(new RaceDto
                {
                    Id = (int)Race.Halfing,
                    Name = Race.Halfing.ToString()
                });

            return values;
        }

        #endregion PrivateMethods

        #region SignalRMethods

        private void SendMessage(int characterId, string message) => Task.Run(async () => await characterHub.SendMessage(characterId, message));

        private void SendMessageAboutExperienceChange(Character character) => Task.Run(async ()
            => await characterHub.ChangeExperience(character.Id, character.ExperienceSummary - character.ExperienceUsed));

        private void SendMessageAboutExperienceSummaryChange(Character character) => Task.Run(async () => await characterHub.ChangeExperienceSummary(character.Id, character.ExperienceSummary));

        private void SendMessageAboutMoneyChange(Character character)
        {
            var money = MoneyCalculator.ConvertMoney(character.Money);

            Task.Run(async () => await characterHub.ChangeMoney(character.Id, money.Gold, money.Silver, money.Bronze));
        }

        private void SendMessageAboutStatisticValueChange(Character character, CharacterStatistic characterStatistic)
        {
            var maximumValue = GetTotalStatisticMaximumValue(characterStatistic.Statistic.Type, character, out int bonusesValue);
            var canBeDecreased = characterStatistic.CurrentValue > characterStatistic.BaseValue + bonusesValue;
            var canBeIncreased = characterStatistic.CurrentValue < maximumValue;

            Task.Run(async () => await characterHub.ChangeStatisticValue(character.Id, characterStatistic.Statistic.Type, characterStatistic.CurrentValue, maximumValue, canBeDecreased, canBeIncreased));

            if (characterStatistic.Statistic.Type == StatisticType.Stamina)
                SendMessageAboutStatisticValueChange(character, StatisticType.Strength, characterStatistic.CurrentValue / 10);
            else if (characterStatistic.Statistic.Type == StatisticType.Resistance)
                SendMessageAboutStatisticValueChange(character, StatisticType.Hardness, characterStatistic.CurrentValue / 10);
        }

        private void SendMessageAboutStatisticValueChange(Character character, StatisticType type, int value)
            => Task.Run(async () => await characterHub.ChangeStatisticValue(character.Id, type, value, value, false, false));

        #endregion SignalRMethods
    }
}