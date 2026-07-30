using Ashburn.World;

namespace Ashburn.Cutscenes
{
    /// <summary>Defines the authored order of story dialogue independently of scene access.</summary>
    public static class StoryProgression
    {
        public const string CorpFirstEscapeComplete = "Story:CorpFirstEscapeComplete";
        public const string PoliceInvestigationComplete = "Story:PoliceInvestigationComplete";
        public const string GasStationComplete = "Story:GasStationComplete";
        public const string CompanySecondVisitComplete = "Story:CompanySecondVisitComplete";
        public const string HangarComplete = "Story:HangarComplete";

        public static bool CanPlay(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
                return false;

            return eventId switch
            {
                "corp_first_warden_001" =>
                    WorldState.Has("dialogue:corp_lobby_enter_001"),

                "hospital_enter_001" or "hospital_record_001" =>
                    WorldState.Has(CorpFirstEscapeComplete),

                "police_enter_001" or
                "police_sentil_files_001" or
                "police_fire_calls_001" =>
                    WorldState.Has("HospitalRecordRead"),

                "police_exit_001" =>
                    WorldState.Has(PoliceInvestigationComplete),

                "gas_first_monster_001" or
                "gas_lure_plan_001" or
                "gas_keycard_001" =>
                    WorldState.Has(PoliceInvestigationComplete),

                "corp2_floor_plan_001" or
                "corp2_elevator_broken_001" or
                "corp2_need_document_001" or
                "corp2_noise_system_doc_001" =>
                    WorldState.Has(GasStationComplete),

                "hangar_enter_001" or
                "hangar_button_001" or
                "hangar_warden_doc_001" or
                "hangar_power_ready_001" or
                "hangar_awakening_001" =>
                    WorldState.Has(CompanySecondVisitComplete),

                "cityhall_1f_enter_001" or
                "cityhall_2f_crouch_001" or
                "cityhall_3f_device_001" =>
                    WorldState.Has(HangarComplete),

                "ending_shutdown_001" =>
                    WorldState.Has(HangarComplete) && WorldState.Has("TruthDevicePlayed"),

                _ => true,
            };
        }

        public static void Complete(string eventId)
        {
            switch (eventId)
            {
                case "corp_after_escape_001":
                    WorldState.Raise(CorpFirstEscapeComplete);
                    break;

                case "police_sentil_files_001":
                case "police_fire_calls_001":
                    RaiseWhenAll(
                        PoliceInvestigationComplete,
                        "SentilFilesRead",
                        "FireCallsRead");
                    break;

                case "gas_keycard_001":
                    WorldState.Raise(GasStationComplete);
                    break;

                case "corp2_floor_plan_001":
                case "corp2_elevator_broken_001":
                case "corp2_noise_system_doc_001":
                    RaiseWhenAll(
                        CompanySecondVisitComplete,
                        "BasementPlanRead",
                        "ElevatorInspected",
                        "NoiseSystemDocRead");
                    break;

                case "hangar_awakening_001":
                    WorldState.Raise(HangarComplete);
                    break;
            }
        }

        static void RaiseWhenAll(string result, params string[] requirements)
        {
            foreach (var requirement in requirements)
                if (!WorldState.Has(requirement))
                    return;

            WorldState.Raise(result);
        }
    }
}
