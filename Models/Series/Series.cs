using System;
using Microsoft.AspNetCore.Http;

namespace suara_belajar.Models
{
    // ===================== ITEM UNTUK TABLE (load list) =====================
    public class SeriesListItem
    {
        public string SeriesId { get; set; }
        public string PackageId { get; set; }
        public string PackageName { get; set; }
        public string Name { get; set; }
        public string CoverImage { get; set; }
        public int Sequence { get; set; }
        public DateTime? DeletedDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    // ===================== DETAIL (get by id, populate form Edit) =====================
    public class SeriesDetail
    {
        public string SeriesId { get; set; }
        public string PackageId { get; set; }
        public string Name { get; set; }
        public string CoverImage { get; set; }
        public int Sequence { get; set; }
    }

    // ===================== REQUEST: SAVE (create / update, multipart/form-data) =====================
    public class SeriesSaveRequest
    {
        public bool IsEdit { get; set; }
        public string SeriesId { get; set; }
        public string PackageId { get; set; }
        public string Name { get; set; }
        public int Sequence { get; set; }

        public IFormFile CoverFile { get; set; }
    }
}