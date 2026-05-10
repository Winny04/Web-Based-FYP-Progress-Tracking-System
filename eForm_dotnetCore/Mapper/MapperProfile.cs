using AutoMapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.ViewModels;
using static eForm_dotnetCore.Controllers.RFQSharingController;
using static eForm_dotnetCore.ViewModels.BOMExcelModel;

namespace eForm_dotnetCore.Mapper
{
    public class MapperProfile:Profile
    {

        public MapperProfile()
        {
            //CreateMap<Supplier, GridQuotation>(MemberList.None)
            //    .ForMember(x => x.supplierName, opt =>
            //        opt.MapFrom(src => src.Name));

            CreateMap<Project, ProjectZone>(MemberList.None)
                .ForMember(x => x.ProjectCode, opt => opt.MapFrom(src => src.Code))
                .ForMember(x => x.Customer, opt => opt.MapFrom(src => src.Customer))
                .ForMember(x => x.ProjectPoNo, opt => opt.MapFrom(src => src.PoNumber))
                .ForMember(x => x.ProjectCategory, opt => opt.MapFrom(src => src.CategoryInfo.CategoryName))
                .ForMember(x => x.ProjectQuantity, opt => opt.MapFrom(src => src.PoQuantity))
                .ForMember(x => x.BudgetMaterialCost, opt => opt.MapFrom(src => src.BudgetMaterialCost))
                .ForMember(x => x.ActualMaterialCost, opt => opt.MapFrom(src => src.ActualMaterialCost))
                .ForMember(x => x.BuildBy, opt => opt.MapFrom(src => src.PICS));

            CreateMap<RFQHeader, PRGridHeader>(MemberList.None);
            CreateMap<BOMGrid, PRBOMGrid>(MemberList.None);
            CreateMap<PRBOMGrid, BOMGrid>(MemberList.None);
            CreateMap<PRData, POData>(MemberList.None);
            CreateMap<PRBOMGrid, POBOMGrid>(MemberList.None);
            CreateMap<SupplierData,SupplierVM>(MemberList.None);
            CreateMap<SupplierInfo, SupplierInfoEditModel>(MemberList.None);
            CreateMap<FormVM, Form>(MemberList.None);
            CreateMap<PendingList, BOMGrid>(MemberList.None);
            //CreateMap<RFQRoot, NewRFQRoot>(MemberList.None);
            //CreateMap<RFQData, NewRFQData>(MemberList.None);
            //CreateMap<BOMGrid, NewBOMGrid>(MemberList.None);
            //CreateMap<PRRoot, NewPRRoot>(MemberList.None);
            //CreateMap<PRData, NewPRData>(MemberList.None);
            //CreateMap<PRBOMGrid, NewPRBOMGrid>(MemberList.None);
            //CreateMap<SupplierContainer, NewSupplierContainer>(MemberList.None);

            CreateMap<SupplierSupportFile, SupportFileVM>(MemberList.None);
            CreateMap<PurchaseHistory, PurchaseHistoryVM>(MemberList.None);
            CreateMap<RFQSharing, RSModel>(MemberList.None);
        }
    }
}
