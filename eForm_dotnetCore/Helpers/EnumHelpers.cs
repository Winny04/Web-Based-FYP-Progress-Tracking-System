using eForm_dotnetCore.Common;

namespace eForm_dotnetCore.Helpers
{
    public class EnumHelpers
    {
        public static List<SelectItem> ToSelectList<T>() where T : struct, IComparable
        {
            var selectItems = Enum.GetValues(typeof(T))
                .Cast<T>()
                .Select(x => new SelectItem(Convert.ToInt16(x).ToString(), x.ToString())).ToList();

            return selectItems;
        }
    }
}
