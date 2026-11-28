using System;
using Microsoft.AspNetCore.Http;

namespace suara_belajar.Models
{
    // ===================== ITEM UNTUK TABLE (load list) =====================
    public class AudiobookListItem
    {
        public string AudiobookId { get; set; }
        public string SeriesId { get; set; }
        public string SeriesName { get; set; }
        public string PackageId { get; set; }
        public string PackageName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CoverImage { get; set; }
        public string Duration { get; set; }
        public DateTime? DeletedDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    // ===================== DETAIL (get by id, populate form Edit) =====================
    public class AudiobookDetail
    {
        public string AudiobookId { get; set; }
        public string SeriesId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CoverImage { get; set; }
        public string Duration { get; set; }
    }

    // ===================== REQUEST: SAVE (create / update, multipart/form-data karena ada file) =====================
    public class AudiobookSaveRequest
    {
        public bool IsEdit { get; set; }
        public string AudiobookId { get; set; } // wajib diisi manual saat Create, dipakai sebagai kunci saat Edit
        public string SeriesId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Duration { get; set; }
        public IFormFile CoverFile { get; set; } // opsional
        public IFormFile AudioFile { get; set; } // opsional
    }
}