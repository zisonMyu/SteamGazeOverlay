using System;
using System.Collections.Generic;

namespace SteamGaze {
    // Observations from the validation window, not a count of hardware samples.
    public sealed class MappingCheck {
        long lastSequence=-1;
        public string startedUtc=DateTime.UtcNow.ToString("O"),finishedUtc;
        public int observations,validEyeObservations,desktopHitObservations;
        public string lastStatus,lastHitUtc;
        public DesktopOutput lastObservedHit;
        public bool humanAccuracyConfirmed=false;
        public Dictionary<string,int> reasons=new Dictionary<string,int>();
        public string Result {get{return desktopHitObservations>0?"pipeline_observed_accuracy_unverified":validEyeObservations>0?"eye_received_no_desktop_hit":"waiting_for_valid_eye_tracking";}}
        public void Observe(Snapshot frame){
            lastStatus=frame.desktopStatus;
            if(frame.sequence<=0||frame.sequence==lastSequence)return;
            lastSequence=frame.sequence;observations++;
            string reason=frame.reason??"unknown";int count;reasons.TryGetValue(reason,out count);reasons[reason]=count+1;
            if(!frame.valid)return;
            validEyeObservations++;
            if(frame.desktop!=null){desktopHitObservations++;lastObservedHit=frame.desktop;lastHitUtc=frame.utc;}
        }
    }
}
