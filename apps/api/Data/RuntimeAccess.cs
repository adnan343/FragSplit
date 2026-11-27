using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public static class RuntimeAccess
{
    public static Task Grant(ShopDb db) =>
        db.Database.ExecuteSqlRawAsync(
            """
            GRANT USAGE ON SCHEMA public TO vialmuse_api;
            GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO vialmuse_api;
            GRANT SELECT ON "Accounts", "Audits", "ConfirmationMails", "History", "Images",
                "Lines", "Movements", "Orders", "Perfumes", "SavedCartLines", "Settings",
                "Variants", "Zones" TO vialmuse_api;
            GRANT INSERT ON "Accounts", "Audits", "ConfirmationMails", "History", "Images",
                "Lines", "Movements", "Orders", "Perfumes", "SavedCartLines", "Variants",
                "Zones" TO vialmuse_api;
            GRANT UPDATE ON "Accounts", "ConfirmationMails", "Images", "Orders", "Perfumes",
                "SavedCartLines", "Settings", "Variants", "Zones" TO vialmuse_api;
            GRANT DELETE ON "Images", "SavedCartLines" TO vialmuse_api;
            """
        );
}
