﻿using WarhammerProfessionApp.Utility;

namespace WarhammerProfessionApp.Dtos
{
    public class StatiticValueChangeDto
    {
        public bool IncrementingValue { get; set; }

        public StatisticType Type { get; set; }
    }
}