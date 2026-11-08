"""生成 FlowClip 应用图标（Apple 风格渐变 + 剪贴板卡片）。
输出 16/32/48/64/128/256 多尺寸 .ico，分别放入 App 与 Windows 项目的 Assets 目录。
"""
from PIL import Image, ImageDraw, ImageFilter

S = 256
OUT_SIZES = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]

# 1) 对角线渐变背景（#0A84FF -> #5E5CE6）
grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(grad)
top = (10, 132, 255, 255)
bot = (94, 92, 230, 255)
for y in range(S):
    t = y / (S - 1)
    r = int(top[0] + (bot[0] - top[0]) * t)
    g = int(top[1] + (bot[1] - top[1]) * t)
    b = int(top[2] + (bot[2] - top[2]) * t)
    gd.line([(0, y), (S, y)], fill=(r, g, b, 255))

# 2) 圆角遮罩（带轻微羽化）
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=56, fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(1.2))

base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
base.paste(grad, (0, 0), mask)

# 3) 白色剪贴板卡片 + 顶部夹子 + 文本行
d = ImageDraw.Draw(base)
cx0, cy0, cx1, cy1 = 64, 70, 192, 206
# 卡片阴影
d.rounded_rectangle([cx0 + 3, cy0 + 5, cx1 + 3, cy1 + 5], radius=18, fill=(0, 0, 0, 55))
# 卡片主体
d.rounded_rectangle([cx0, cy0, cx1, cy1], radius=18, fill=(255, 255, 255, 238))
# 顶部夹子
tw, th = 56, 26
tx0 = (S - tw) // 2
d.rounded_rectangle([tx0, cy0 - 12, tx0 + tw, cy0 + th - 6], radius=10, fill=(255, 255, 255, 248))
d.rounded_rectangle([tx0 + 10, cy0 - 18, tx0 + tw - 10, cy0 - 6], radius=6, fill=(255, 255, 255, 248))
# 文本行（浅灰）
line_color = (180, 186, 200, 255)
ly = cy0 + 30
for i in range(3):
    w = (cx1 - cx0 - 36) if i < 2 else (cx1 - cx0 - 70)
    d.rounded_rectangle([cx0 + 18, ly, cx0 + 18 + w, ly + 9], radius=4, fill=line_color)
    ly += 26
# 强调色小方块（暗示图片/链接）
d.rounded_rectangle([cx0 + 18, ly, cx0 + 18 + 34, ly + 26], radius=6, fill=(10, 132, 255, 255))

base.save("F:/Projects/FlowClip/src/FlowClip.App/Assets/icon.ico", format="ICO", sizes=OUT_SIZES)
base.save("F:/Projects/FlowClip/src/FlowClip.Windows/Assets/icon.ico", format="ICO", sizes=OUT_SIZES)
print("icon generated")
