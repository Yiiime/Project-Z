using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    // Explicit test entry only. No substitute dialogue in the normal game.
    public sealed class UnifiedMemorySmokeRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-memorySmokeOutput");if(at<0||at+1>=args.Length)yield break;
            var root=args[at+1];float until=Time.realtimeSinceStartup+60;
            while((GameSession.Instance?.Memory?.Client==null||!GameSession.Instance.Memory.Client.Synchronized)&&Time.realtimeSinceStartup<until)yield return null;
            bool connected=GameSession.Instance?.Memory?.Client?.Synchronized??false;
            File.WriteAllText(root,JsonUtility.ToJson(new Result{connected=connected,actors=SevenCharacterCatalog.Actors.Length,identities=string.Join(",",SevenCharacterCatalog.Actors.Select(a=>a.id))},true));
            if(Application.isBatchMode)Application.Quit(connected?0:1);
        }
        [Serializable] sealed class Result {public bool connected;public int actors;public string identities;}
    }
}
