﻿namespace WarhammerProfessionApp.Dtos
{
    public class CharacterSkillDto : SkillDto
    {
        public byte Level { get; set; }

        public int Value { get; set; }
    }
}