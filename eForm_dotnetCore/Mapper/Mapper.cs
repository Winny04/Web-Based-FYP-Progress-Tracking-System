using AutoMapper;

namespace eForm_dotnetCore.Mapper
{
    public static class Mapper
    {
        internal static IMapper Map { get; set; }

        static Mapper()
        {
            Map = new MapperConfiguration(cfg => cfg.AddProfile<MapperProfile>())
               .CreateMapper();
        }

        public static T ToModel<T>(this object att)
        {
            return Map.Map<T>(att);
        }

        public static List<T> ToListModel<T>(this object atts)
        {
            return Map.Map<List<T>>(atts);
        }
    }
}
