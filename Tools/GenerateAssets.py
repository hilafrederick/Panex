import os
from PIL import Image, ImageDraw, ImageFilter

def create_fluent_assets():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    assets_dir = os.path.join(root_dir, "Panex", "Assets")
    
    # Base high-res cropped asset
    base_src = os.path.join(assets_dir, "logo.png")
    if not os.path.exists(base_src):
        print(f"Error: {base_src} does not exist.")
        return

    base_512 = Image.open(base_src).convert("RGBA")
    
    # 1. Generate multi-resolution app.ico
    sizes = [256, 128, 64, 48, 32, 24, 16]
    ico_images = []
    for s in sizes:
        sub = base_512.resize((s, s), Image.Resampling.LANCZOS)
        if s <= 32:
            sub = sub.filter(ImageFilter.UnsharpMask(radius=1.0, percent=130, threshold=2))
        ico_images.append(sub)
    
    os.makedirs(assets_dir, exist_ok=True)
    ico_path = os.path.join(assets_dir, "app.ico")
    ico_images[0].save(
        ico_path,
        format="ICO",
        append_images=ico_images[1:],
        sizes=[(s, s) for s in sizes]
    )
    print(f"Saved {ico_path}")
    
    root_ico = os.path.join(root_dir, "Panex.ico")
    base_512.resize((256, 256), Image.Resampling.LANCZOS).save(
        root_ico,
        format="ICO",
        append_images=ico_images[1:],
        sizes=[(s, s) for s in sizes]
    )
    print(f"Saved {root_ico}")
    
    # 2. Square44x44Logo.targetsize-24_altform-unplated.png (24x24)
    ico_images[sizes.index(24)].save(os.path.join(assets_dir, "Square44x44Logo.targetsize-24_altform-unplated.png"))
    
    # 3. Square44x44Logo.scale-200.png (88x88)
    base_512.resize((88, 88), Image.Resampling.LANCZOS).save(os.path.join(assets_dir, "Square44x44Logo.scale-200.png"))
    
    # 4. Square150x150Logo.scale-200.png (300x300)
    base_512.resize((300, 300), Image.Resampling.LANCZOS).save(os.path.join(assets_dir, "Square150x150Logo.scale-200.png"))
    
    # 5. LockScreenLogo.scale-200.png (48x48)
    ico_images[sizes.index(48)].save(os.path.join(assets_dir, "LockScreenLogo.scale-200.png"))
    
    # 6. StoreLogo.png (50x50)
    base_512.resize((50, 50), Image.Resampling.LANCZOS).save(os.path.join(assets_dir, "StoreLogo.png"))
    
    # 7. Wide310x150Logo.scale-200.png (620x300, centered on transparent canvas)
    wide_img = Image.new("RGBA", (620, 300), (0, 0, 0, 0))
    logo_for_wide = base_512.resize((220, 220), Image.Resampling.LANCZOS)
    wide_img.paste(logo_for_wide, ((620 - 220) // 2, (300 - 220) // 2), logo_for_wide)
    wide_img.save(os.path.join(assets_dir, "Wide310x150Logo.scale-200.png"))
    
    # 8. SplashScreen.scale-200.png (1240x600, centered on light Fluent canvas)
    splash_img = Image.new("RGBA", (1240, 600), (249, 250, 251, 255))
    logo_for_splash = base_512.resize((320, 320), Image.Resampling.LANCZOS)
    splash_img.paste(logo_for_splash, ((1240 - 320) // 2, (600 - 320) // 2), logo_for_splash)
    splash_img.save(os.path.join(assets_dir, "SplashScreen.scale-200.png"))
    
    print("All Panex assets generated successfully!")

if __name__ == "__main__":
    create_fluent_assets()
