# Warehouse Management Feature - User Guide

## Overview

The Warehouse Management system ensures data organization and prevents operations without proper setup. **All operations require a warehouse to be selected and configured.**

---

## Initial Setup (First Time Only)

### Step 1: Launch Application
- Double-click `GasAgencyManager.exe`
- You'll see this message:

```
⚠️ No warehouses configured.

Please navigate to the Admin tab to add at least one warehouse 
before proceeding with operations.
```

### Step 2: Open Admin Tab
- Click the **Admin** tab at the top
- You'll see three sections:
  1. **Drivers** - Manage driver list
  2. **Vehicles** - Manage vehicle list
  3. **Warehouses** - ← New section

### Step 3: Add Your First Warehouse
In the **Warehouses** section:
1. Click the text field labeled "New Warehouse Name"
2. Type warehouse name (e.g., `Main Warehouse`, `Branch Office`, `Depot A`)
3. Click **Add Warehouse** button
4. See success message: "Warehouse added."
5. Warehouse appears in the list below

### Step 4: Return to Operations
- All other tabs are now **enabled** ✅
- Warehouse dropdown at top will show your new warehouse
- Select it to begin operations

---

## Daily Operations

### Before Starting Work

#### 1. Select Warehouse (Always First!)
- Look at the top of the window
- Find **Warehouse Selector** dropdown
- Choose the warehouse you'll be working with
- All operations connect to this warehouse

#### 2. Verify Warehouse Selection
- **When Warehouse is Selected:**
  - ✅ All operation buttons are **enabled**
  - ✅ Stock grids show correct warehouse data
  - ✅ Morning/Evening reconciliation buttons active

- **When No Warehouse Selected:**
  - ❌ All operation buttons are **disabled**
  - ❌ Stock grids are **empty**
  - ❌ You cannot proceed with operations

#### Important Note
**Every operation (Save, Receive, Return) automatically logs which warehouse it affected.**

---

## Managing Warehouses

### Adding Additional Warehouses

1. Go to **Admin** tab
2. Scroll to **Warehouses** section
3. Enter warehouse name
4. Click **Add Warehouse**
5. New warehouse appears in:
   - Admin tab warehouse list
   - Warehouse dropdown selector (at top)

**Example Warehouse Names:**
- `Gandhinagar Main`
- `City Branch`
- `Vadodara Depot`
- `Regional Hub - North`
- `Backup Storage`

### Removing Warehouses

**Warning**: Removing a warehouse **deletes all related data**:
- ❌ All stock for that warehouse (FilledQty, EmptyQty)
- ❌ All transaction history for that warehouse
- ❌ All reconciliation records

**Steps to Remove:**
1. Go to **Admin** tab
2. Scroll to **Warehouses** section
3. Select warehouse from list (click to highlight)
4. Click **Remove Selected Warehouse**
5. Confirmation dialog appears
6. Click **Yes** to confirm deletion
7. Warehouse is removed

**If You Accidentally Deleted:**
- Unfortunately, there's no undo for warehouse deletion
- Data is permanently lost
- Contact administrator for possible backup restoration

---

## Warehouse Selection Behavior

### When Warehouse Changes
Automatically updates:
- ✅ Morning Operations stock display
- ✅ Evening Reconciliation data
- ✅ IOCL Plant Purchases stock
- ✅ All relevant grids and dropdowns

### Warehouse Restrictions
- **Cannot**: Start operations without selecting warehouse
- **Cannot**: See stock for unselected warehouse
- **Must**: Select warehouse for each work session

### Per-Warehouse Independence
Each warehouse tracks independently:
- Cylinder inventory (Filled/Empty)
- Daily operations
- Evening reconciliation
- Transaction history
- Stock reports

---

## Common Scenarios

### Scenario 1: Multiple Warehouses, Same Company
**Setup**: Create separate warehouse for each location
```
- Warehouse: "Mumbai - Bandra"
- Warehouse: "Mumbai - Powai" 
- Warehouse: "Pune - Main"
```
**Select**: Choose appropriate warehouse at start of day
**Benefit**: Complete inventory separation per location

### Scenario 2: Multiple Shifts, Single Warehouse
**Setup**: Single warehouse named with location
```
- Warehouse: "Plant A"
```
**Select**: Same warehouse for all shifts
**Data**: All shifts' transactions in one warehouse history

### Scenario 3: Temporary vs Permanent Storage
**Setup**: Create warehouses for different purposes
```
- Warehouse: "Main Storage"
- Warehouse: "Temporary" (for testing/demo)
```
**Recommendation**: Use Temporary for non-production data

---

## Admin Reference: Warehouse Database

### Where Data is Stored
- **Database**: `AgencyDB.db` (SQLite)
- **Table**: `Warehouses` (list of warehouses)
- **Related Tables**: 
  - `StockMaster` (linked by WarehouseId)
  - `TransactionLogs` (linked by WarehouseId)

### What's Stored Per Warehouse
```
ID (Auto)     | Name                  | Related Data
1             | Main Warehouse        | 1000 FilledQty, 200 EmptyQty
2             | Branch Office         | 500 FilledQty, 100 EmptyQty
3             | Regional Distribution | 2000 FilledQty, 400 EmptyQty
```

---

## Troubleshooting

### Problem: "Cannot add warehouse - empty name"
**Solution**: The warehouse name field is empty. Type a name and try again.

### Problem: "All tabs are disabled except Admin"
**Solution**: No warehouses exist. Add one in Admin tab → Warehouses section.

### Problem: "Operations are disabled even with warehouse selected"
**Solution**: Try re-selecting the warehouse from the dropdown.

### Problem: " Can't find my warehouse in dropdown"
**Possible Cause**: 
- Warehouse is in a different database file
- Application is reading from wrong database location
**Solution**: Verify `AgencyDB.db` is in correct path

---

## Keyboard Shortcuts (Optional)

When in Warehouses section:
- **Tab**: Move to next field
- **Enter**: Click Add Warehouse button
- **Delete**: Remove selected warehouse (if selected in list)

---

## Questions?

**Common Questions Answered:**

**Q: Can I rename a warehouse?**  
A: Not currently. Workaround: Remove old, add new, adjust data manually if needed.

**Q: Can I merge two warehouses' data?**  
A: Not automatically. Manual data consolidation would be required.

**Q: How many warehouses can I create?**  
A: Theoretically unlimited. Performance depends on database size.

**Q: Do users see all warehouses or only their assigned one?**  
A: Currently all users see all warehouses. Admin should communicate which to use.

**Q: What if someone deletes a warehouse by mistake?**  
A: Contact IT/Admin to restore from backup. Always maintain regular backups.

---

## Best Practices

✅ **DO:**
- Create warehouses by location/branch
- Use clear, descriptive names
- Backup database regularly before making changes
- Verify warehouse selection before starting operations
- Document your warehouse structure

❌ **DON'T:**
- Create duplicate warehouses (e.g., "Warehouse 1", "Warehouse 2", "Warehouse 1")
- Delete warehouses with active data without backup
- Assume warehouse will stay selected (verify each session)
- Share databases between different companies

---

**Version**: 1.0  
**Last Updated**: 2026-07-14  
**Status**: Ready for Production ✅


