using Newtonsoft.Json.Linq;

namespace Cutie
{
    internal static class BatchSchema
    {
        internal static JObject Create() => JObject.Parse(@"{
          'type':'object','additionalProperties':false,'required':['operations'],
          'properties':{
            'operations':{'type':'array','minItems':1,'maxItems':1000,'items':{
              'type':'object','additionalProperties':false,'required':['id','tool'],
              'properties':{
                'id':{'type':'string','pattern':'^[A-Za-z0-9_-]{1,64}$'},
                'tool':{'type':'string','description':'An existing non-control tool name; inspect list_batch_capabilities.'},
                'arguments':{'type':'object','description':'Existing tool arguments. A value may be {""$ref"":""step#/field""} (JSON Pointer) or {""$handle"":""name"",""property"":""trackIndex""}. Only previous successful steps can be referenced.'},
                'capture':{'type':'array','items':{
                  'type':'object','additionalProperties':false,'required':['name','kind','selector'],
                  'properties':{
                    'name':{'type':'string','pattern':'^[A-Za-z0-9_-]{1,64}$'},
                    'kind':{'type':'string','enum':['track','event','effect','marker','region']},
                    'selector':{'type':'object','description':'track: trackIndex; event: trackIndex,eventIndex; effect: targetType,trackIndex,effectIndex,eventIndex when targetType=event; marker/region: index. May reference this step result. Handles dynamically resolve indices and fail after deletion or project replacement.'}
                  }
                }}
              }
            }},
            'label':{'type':'string','default':'Cutie batch','minLength':1,'maxLength':128},
            'undoMode':{'type':'string','enum':['single','staged'],'default':'single'},
            'onError':{'type':'string','enum':['stop','continue'],'default':'stop'},
            'batchId':{'type':'string','pattern':'^[A-Za-z0-9_-]{1,64}$','description':'Optional caller-assigned ID for status/cancellation. Duplicate IDs are rejected while retained.'},
            'completionTimeoutSeconds':{'type':'integer','minimum':1,'maximum':3600,'default':120}
          }
        }");
    }
}
