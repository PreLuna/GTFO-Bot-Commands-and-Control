using System;
using System.Collections.Generic;
namespace Hikaria.BoosterTweaker.Managers {
    public static class BoosterImplantTemplateManager {
        public static bool DisableBoosterConditions {get;set;}
        public sealed class EffectTemplate {}
        public static void ApplyPerfectBoosterFromTemplate(BoosterImplant implant,List<EffectTemplate> effects,List<uint> conditions) {
            implant.Conditions=new(DisableBoosterConditions?Array.Empty<uint>():conditions.ToArray());
        }
    }
}
namespace TheArchive.Core.FeaturesAPI {
    public static class FeatureManager {
        public static bool Active=true,ThrowQuery;
        public static bool IsFeatureEnabled(string feature,bool useGuid) {
            if(feature!="Hikaria.BoosterTweaker.Features.CustomBooster"||!useGuid)throw new Exception("wrong feature lookup");
            if(ThrowQuery)throw new Exception("mock-feature-query-failure");
            return Active;
        }
    }
}
