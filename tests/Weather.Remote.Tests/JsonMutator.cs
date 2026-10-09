using System.Text;
using System.Text.Json.Nodes;

namespace Weather.Remote.Tests;

/// <summary>JSON の 1 か所を壊す(削除・null・型違い・空・極端な値)。サーバーの誤った応答の再現用(固定シードで決定的)。</summary>
internal static class JsonMutator{
    public static string Mutate(string json,Random random){
        var root=JsonNode.Parse(json)!;
        var nodes=new List<(JsonNode? Parent,object? Key)>();
        void Walk(JsonNode? parent,object? key,JsonNode? node){
            nodes.Add((parent,key));
            if(node is JsonObject o){
                foreach(var pair in o.ToList()){
                    Walk(o,pair.Key,pair.Value);
                }
            }else if(node is JsonArray a){
                for(var i=0;i<a.Count;i++){
                    Walk(a,i,a[i]);
                }
            }
        }
        Walk(null,null,root);
        var (parent,key)=nodes[random.Next(nodes.Count)];
        if(parent is null){
            return json;
        }
        var op=random.Next(7);
        JsonNode? replacement=null;
        switch(op){
            case 2:
                replacement=JsonValue.Create("x");
                break;
            case 3:
                replacement=JsonValue.Create(-1);
                break;
            case 4:
                if(random.Next(2)==0){
                    replacement=new JsonArray();
                }else{
                    replacement=new JsonObject();
                }
                break;
            case 5:
                replacement=JsonValue.Create(1e308);
                break;
            case 6:
                replacement=JsonValue.Create(true);
                break;
        }
        if(parent is JsonObject po){
            if(op==0){
                po.Remove((string)key!);
            }else{
                po[(string)key!]=replacement;
            }
        }else if(parent is JsonArray pa){
            var index=(int)key!;
            if(op==0){
                pa.RemoveAt(index);
            }else{
                pa[index]=replacement;
            }
        }
        return root.ToJsonString();
    }
}
