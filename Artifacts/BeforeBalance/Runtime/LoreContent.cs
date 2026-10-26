using System.Linq;
using System;

namespace HackThePlanet
{
    [Serializable]
    public sealed class LoreFragment
    {
        public string id, title, company, contract, text;
    }

    public static class LoreContent
    {
        public static LoreFragment For(Contract job,int index) => job.lore!=null&&index>=0&&index<job.lore.Length?job.lore[index]:null;
        public static LoreFragment Find(Contract job,string id) => string.IsNullOrEmpty(id)?null:job?.lore?.FirstOrDefault(x=>x.id==id);
    }
}
