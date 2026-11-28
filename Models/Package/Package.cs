using System;
using Microsoft.AspNetCore.Http;

namespace suara_belajar.Models
{
    // ===================== ENTITY (mapping ke tabel mst_package) =====================
    public class Package
    {
        public string PackageId { get; set; }
        public string Name { get; set; }
        public string LogoImage { get; set; }
        public DateTime? DeletedDate { get; set; }
        public bool IsSeries { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    // ===================== ITEM UNTUK TABLE (dipakai di dalam ResponseDto.Data saat load list) =====================
    public class PackageListItem
    {
        public string PackageId { get; set; }
        public string Name { get; set; }
        public string LogoImage { get; set; }
        public DateTime? DeletedDate { get; set; }
        public bool IsSeries { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    // ===================== DETAIL (dipakai di dalam ResponseDto.Data saat get by id) =====================
    public class PackageDetail
    {
        public string PackageId { get; set; }
        public string Name { get; set; }
        public string LogoImage { get; set; }
        public bool IsSeries { get; set; }
    }

    // ===================== REQUEST: SAVE (create / update, multipart/form-data karena ada file) =====================
    public class PackageSaveRequest
    {
        public string PackageId { get; set; }
        public string Name { get; set; }
        public bool IsSeries { get; set; }
        public bool IsEdit { get; set; }
        public IFormFile LogoFile { get; set; } // opsional, nullable saat edit tanpa ganti logo
        public string ExplorerStylingVersion { get; set; }
    }
}