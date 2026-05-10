using eForm_dotnetCore.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using NPOI.OpenXmlFormats.Vml;
using System.Collections.ObjectModel;

namespace eForm_dotnetCore.Common
{
    public class Departments
    {
        private readonly EformSophicDemoContext _context = new EformSophicDemoContext();

        public static List<SelectListItem> DepartmentList()
        {
            var list = new List<SelectListItem> {
                    new SelectListItem{ Text="BD", Value = "BD" },
                    new SelectListItem{ Text="FE", Value = "FE" },
                    new SelectListItem{ Text="FAS", Value = "FAS" },
                    new SelectListItem{ Text="OE", Value = "OE" },
                    new SelectListItem{ Text="SI", Value = "SI" },
                    new SelectListItem{ Text="R&D", Value = "R&D" },
                    new SelectListItem{ Text="Mechanical", Value = "Mechanical" },
            };

            return list;
        }


        public static Dictionary<string, string> DepartmentDictionary()
        {
            var dict = new Dictionary<string, string>();
            dict.Add("BD", "BD");
            dict.Add("FIN", "FE");
            dict.Add("FSE", "FAS");
            dict.Add("OE", "OE");
            dict.Add("SI", "SI");
            dict.Add("R&D", "R&D");
            dict.Add("FAS", "FAS");
            dict.Add("FE", "FE");
            dict.Add("Mechanical", "Mechanical");

            return dict;

        }

        public static string ConvertRequestorWithDept(string requestor)
        {
            if (String.IsNullOrEmpty(requestor))
            {
                return "";
            }
            string RequestorDept = requestor.Split('(', ')')[1];
            var departmentString = Departments.DepartmentDictionary();
            var modRequestorDept = departmentString[RequestorDept];
            var tempRequestor = requestor.Substring(0, requestor.IndexOf("(") + 1);
            var modRequestor = tempRequestor + modRequestorDept + ")";
            return modRequestor;
        }

        public void UpdatePRRequestorDept(int? prId, string newRequestor)
        {
            try
            {
                if (!prId.HasValue || prId == 0)
                {
                    return;
                }

                var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
                if (pr != null)
                {
                    PRRoot root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                    root.data.header.FirstOrDefault().requester = newRequestor;

                    pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                    _context.Prs.Update(pr);
                    _context.SaveChanges();
                }
            }
            catch (Exception ex)
            {

            }
        }

        public void UpdatePRDepartment(int? prId, string newDepartment)
        {
            try
            {
                if (!prId.HasValue || prId == 0)
                {
                    return;
                }

                var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
                if (pr != null)
                {
                    PRRoot root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                    root.data.header.FirstOrDefault().department = newDepartment;

                    pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                    _context.Prs.Update(pr);
                    _context.SaveChanges();
                }
            }
            catch (Exception ex)
            {

            }
        }

        public void UpdateRFQRequestorDept(int? rfqId, string newRequestor)
        {
            try
            {
                if (!rfqId.HasValue || rfqId == 0)
                {
                    return;
                }

                var rfq = _context.Rfqs.Where(x => x.Id == rfqId).FirstOrDefault();
                if (rfq != null)
                {
                    RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                    root.data.header.FirstOrDefault().requester = newRequestor;

                    rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                    _context.Rfqs.Update(rfq);
                    _context.SaveChanges();
                }
            }
            catch (Exception ex)
            {

            }
        }

    }
}
