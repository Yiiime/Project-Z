using System;
using UnityEngine;
namespace Seabreeze
{
    [CreateAssetMenu(menuName = "Seabreeze/Story Event")]
    public sealed class StoryEventData : ScriptableObject
    {
        public string id;
        public int minimumDay = 1;
        public TimePeriod earliestPeriod;
        public bool anyPlace = true;
        public PlaceId place;
        public string[] requiredFlags = Array.Empty<string>();
        public string[] forbiddenFlags = Array.Empty<string>();
        [TextArea] public string notification;
        public StoryEffect[] effects = Array.Empty<StoryEffect>();
    }
}
