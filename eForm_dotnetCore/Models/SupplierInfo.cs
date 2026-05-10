namespace eForm_dotnetCore.Models
{
    public class SupplierInfo
    {
        public Guid Id { get; set; }
        public string VendorName { get; set; }
        public string? DataResponse { get; set; }
        public bool Completed { get; set; } = false;
        public List<SupplierAttachment> SupplierAttachments { get; set; } = new List<SupplierAttachment>();
        public List<SupplierSupportFile> SupplierSupportFiles { get; set; } = new List<SupplierSupportFile>();
        //public string? Code { get; set; }
        //public string? VendorName { get; set; }
        //public string? Address { get; set; }
        //public string? Phone1 { get; set; }
        //public string? Phone2 { get; set; }
        //public string? Fax { get; set; }
        //public string? Email { get; set; }
        //public string? WebSite { get; set; }
        //public string? Attn { get; set; }
        //public string? Terms { get; set; }
        //public string? Currency { get; set; }
    }

    public class SupplierRoot
    {
        public SupplierRoot()
        {
            data = new SupplierData();
        }

        public SupplierData data { get; set;}
    }

    public class SupplierVM : SupplierData
    {
        public Guid Id { get; set; }
        public string Completed { get; set; }
    }

    public class SupplierData
    {
        public string? registerType { get; set; }
        public string? supplierType { get; set; }
        //section 1
        public string? companyName { get; set; }
        public string? address { get; set; }
        public string? telephoneNumber { get; set; }
        public string? faxNumber { get; set; }
        public string? emailAddress { get; set; }
        public string? websiteAddress { get; set; }
        public string? typeOfBusiness { get; set; }
        public string? dateInCorporation { get; set; }
        public int? directNoEmployee { get; set; }
        public int? overheadNoEmployee { get; set; }
        public string? lmwLicense { get; set; }
        //section 2
        public double? authorizeCapital { get; set; }
        public double? paidUpCapital { get; set; }
        public double? workingCapital { get; set; }
        public double? previousTurnover { get; set; }
        public double? currentTurnover { get; set; }
        public double? previousProfit { get; set; }
        public double? currentProfit { get; set; }
        //section 3
        public string? businessService { get; set; }
        public string? otherService { get; set; }
        public List<AssetList>? assetList { get; set; }
        //section 4
        public List<CompanyRecord>? trackRecord { get; set; }
        //section 5
        public string? bankName { get; set; }
        public string? bankAddress { get; set; }
        public string? beneficiaryName { get; set; }
        public string? BIC { get; set; }
        public string? IBAN { get; set; }
        public string? accountCurrency { get; set; }
        public string? otherCurrency { get; set; }
        public string? accountNumber { get; set; }
        public string? registrationNumber { get; set; }
        public string? paymentMode { get; set; }
        public string? otherPayment { get; set; }
        public string? paymentTerm { get; set; }
        public string? otherPaymentTerm { get; set; }
        public double? creditTerms { get; set; }
        public double? otherCreditTerm { get; set; }
        public string? incoterm { get; set; }
        public string? otherIncoterm { get; set; }
        public string? ChargesBy { get; set; }
        public string? otherChangesBy { get; set; }
        //section 6
        public string? MPName { get; set; }
        public string? MPDesignation { get; set; }
        public string? MPContact { get; set; }
        public string? MPEmail { get; set; }
        public string? SRName { get; set; }
        public string? SRDesignation { get; set; }
        public string? SRContact { get; set; }
        public string? SREmail { get; set; }
        public string? FRName { get; set; }
        public string? FRDesignation { get; set; }
        public string? FRContact { get; set; }
        public string? FREmail { get; set; }

        //section 11
        public string? registrationCode { get; set; }
    }

    public class AssetList
    {
        public string? equipment { get; set; }
        public string? serialNumber { get; set; }
        public int? quantity { get; set; }
    }

    public class CompanyRecord
    {
        public string? clientCompanyName { get; set; }
        public string? typeOfBusiness { get; set; }
        public int? yearSupplied { get; set; }
        public string? annualSS { get; set; }
        public string? awardReceived { get; set; }
    }
}
