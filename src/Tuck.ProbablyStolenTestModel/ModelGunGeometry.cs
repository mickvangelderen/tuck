// GunModDirectory shape methods ported from the game's d902ccf snapshot.
// Sprite/stat effects are outside the headless transfer model.
#nullable disable
namespace Tuck.ProbablyStolenTestModel;

public static class ModelGunGeometry
{
    public static void InitAttachment(GameItem item)
    {
        if (item.identifier is not ("silencer" or "silencer2" or "silencer_makeshift" or "barrel"
            or "compensator" or "bipod" or "gl" or "grip" or "mag_10mm_ext" or "mag_10mm_smg"
            or "mag_10mm_smg_high" or "mag_10mm_smg_box" or "mag_22_ext" or "stock" or "short_stock"))
            throw new NotSupportedException("Unknown gun attachment: " + item.identifier);
        item.parentItemShapeModifierForItemFunc = Contribution;
    }

    public static bool IsSupported(GameItem item) => item.parentItemShapeModifierForItemFunc == Contribution;

    private static GridShape Contribution(GameItem item, GameInventory inventory, SlotMarker slot,
        GridShape shape, TagSystem state, GameItem parent) => item.identifier switch
    {
        "silencer" => SetSilencerModShape(parent, item),
        "silencer2" => SetSilencerAdvShape(parent, item),
        "silencer_makeshift" => SetSilencerMakeshiftShape(parent, item),
        "barrel" => SetBarrelShape(parent, item),
        "compensator" => SetCompensatorShape(parent, item),
        "grip" => SetGripShape(parent, item),
        "mag_10mm_ext" => Set10MMMagExtShape(parent, item),
        "mag_10mm_smg_box" => SetSMGMagShape(parent, item),
        "mag_22_ext" => Set22MagExtShape(parent, item),
        "stock" => SetStockShape(parent, item),
        "short_stock" => SetShortStockShape(parent, item),
        "bipod" or "gl" => parent.identifier == "smg" ? new GridShapeBuilder()
            .SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(3, 2, false, 0) : null,
        "mag_10mm_smg" => parent.identifier == "smg" ? new GridShapeBuilder()
            .SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(3, 1, false, 0) : null,
        "mag_10mm_smg_high" => parent.identifier == "smg" ? new GridShapeBuilder()
            .SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(2, 2, false, 0) : null,
        _ => throw new NotSupportedException("Unknown gun attachment: " + item.identifier)
    };

    // GameItem.ComputeModifiedShapeState's geometry subset; no speculative additions/removals.
    public static void Resolve(GameItem parent) => Resolve(parent, new HashSet<GameItem>());

    private static void Resolve(GameItem parent, HashSet<GameItem> ancestors)
    {
        if (!ancestors.Add(parent)) throw new NotSupportedException("Cyclic item containment.");
        var contributions = new List<GridShape>();
        foreach (var inventory in GraphUtils.FindChildrenType<GameInventory>(parent))
            foreach (var item in inventory.childItems)
            {
                Resolve(item, ancestors);
                if (item.parentItemShapeModifierForItemFunc == null) continue;
                if (!IsSupported(item)) throw new NotSupportedException("Unknown parent shape callback.");
                var contribution = Contribution(item, inventory, null, item.modifiedShape, item.state, parent);
                if (contribution != null) contributions.Add(contribution);
            }
        ancestors.Remove(parent);
        if (contributions.Count == 0) return;
        var merged = new GridShapeBuilder(parent.shape);
        var a = parent.modifiedShape.ForwardTransform(parent.modifiedXOrigin, parent.modifiedYOrigin);
        var b = parent.modifiedShape.ForwardTransform(parent.modifiedXOrigin + parent.shape.width - 1,
            parent.modifiedYOrigin + parent.shape.height - 1);
        merged.SetTransform(Math.Min(a.Item1, b.Item1), Math.Min(a.Item2, b.Item2),
            parent.modifiedShape.flipped, parent.modifiedShape.orientation);
        var origin = merged.MergeWithLocalShapes(contributions);
        parent.modifiedShape = merged;
        parent.modifiedXOrigin = origin.Item1;
        parent.modifiedYOrigin = origin.Item2;
    }
	private static GridShapeBuilder SetSilencerModShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "printed_gun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "heavy_handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "glock_receiver"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetSilencerAdvShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "printed_gun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "heavy_handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "glock_receiver"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetSilencerMakeshiftShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "printed_gun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "heavy_handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "glock_receiver"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetBarrelShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetCompensatorShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "printed_gun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "heavy_handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "glock_receiver"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(4, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height, 1).SetTransform(7, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetGripShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width + 1, item.modifiedShape.height, 1).SetTransform(4, 1, flipped: false, 0);
		}
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width + 1, item.modifiedShape.height, 1).SetTransform(3, 1, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder Set10MMMagExtShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "heavy_handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(0, 0, flipped: false, 0);
		}
		if ((parent.identifier == "glock_receiver"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(0, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetSMGMagShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(2, 1, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder Set22MagExtShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "printed_gun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(0, 0, flipped: false, 0);
		}
		if ((parent.identifier == "handmade_pistol"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(0, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetStockShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(-2, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(-2, 0, flipped: false, 0);
		}
		return null;
	}

	private static GridShapeBuilder SetShortStockShape(GameItem parent, GameItem item)
	{
		if ((parent.identifier == "shotgun"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(-1, 0, flipped: false, 0);
		}
		if ((parent.identifier == "smg"))
		{
			return new GridShapeBuilder().SetDataFill(item.modifiedShape.width, item.modifiedShape.height + 1, 1).SetTransform(-1, 0, flipped: false, 0);
		}
		return null;
	}
}
