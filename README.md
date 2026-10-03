# Tomb Raider II Level Parser & Viewer

Bu proje, efsanevi **Tomb Raider II (1997)** oyununun `.TR2` uzantılı bölüm (level) dosyalarını okumak, ayrıştırmak ve **OpenTK** (OpenGL) kullanarak 3D ortamda görselleştirmek için geliştirilmiş bir C# projesidir.

Projenin temel amacı, orijinal oyun motorunun kullandığı ikili (binary) veri yapılarını modern bir nesne modeline aktarmak ve bu verileri GPU üzerinde yeniden oluşturmaktır.

## 🚀 Özellikler (Şu Anki Durum)

Projenin merkezinde yer alan `TR2Level.cs` sınıfı, standart TR2 spesifikasyonlarına göre aşağıdaki verileri ayrıştırır:

* **Oda Geometrisi (Rooms):** Odaların köşe (vertex), dörtgen (quad) ve üçgen (triangle) verileri, portallar, sektörler, ışıklar ve alternatif (flipmap) odalar.
* **Dokular (Textures):** 8-bit ve 16-bit doku sayfaları (Textiles), paletler ve nesnelerin UV koordinat eşleştirmeleri.
* **Modeller ve Statik Objeler:** Lara, düşmanlar, kapılar ve odalardaki sabit dekoratif objeler (dokulu ve renkli yüzler).
* **Işıklandırma (Lighting):** Vertex tabanlı oda ışıklandırması, statik obje ve varlık (entity) ışık değerleri.
* **Animasyon:** İskelet sistemi (Mesh Trees), animasyonlar, kareler (Frames), durum değişiklikleri ve animasyon komutları.
* **Zemin Verisi (FloorData):** Odalar arası geçişler için portal kayıtları.

Görüntüleyici (`TRViewer.cs`) odaları ve statik objeleri çizer; varlıklar (düşmanlar, kapılar...) `TRAnimator.cs` ile varsayılan animasyonlarını oynatır. Lara `TRLaraController.cs` ile oyundaki gibi yönetilir: tuşlar hedef durumu belirler, animasyon geçişleri oyunun kendi durum makinesi verisinden (StateChanges / AnimDispatches) gelir, hareket ve zıplama hızları animasyon verisinden (hız alanları ve AnimCommands) alınır, zemin eğimleri FloorData'dan hesaplanır (dik eğimlerde Lara kayar), su odalarında yüzer (`TRLaraController.Swim.cs`) ve kamera Lara'yı arkadan takip eder. Anahtar kareler arasında dönüşler küresel (slerp), kök kayması doğrusal ara değerlenir.

### Henüz Desteklenmeyenler

* Sprite'lar, kameralar ve ses kaynakları dosyada atlanır (okunmaz).
* Yapay zekâ verileri (Boxes, Overlaps, Zones) ve hareketli dokular atlanır.
* Varlıklardan sonraki bölüm (lightmap, sinematik kareler, demo verisi, ses haritası ve örnekleri) okunmaz.
* Varlıklardan sadece tekne katıdır (üstüne düşen Lara oturur); köprü, platform gibi diğer nesnelerin çarpışması yoktur. Tekne sürülemez.
* Lara maymun barlarına tutunamaz; sığ suda yürüme (wade) animasyonu yok; nefes/hasar sistemi yok; su altı renk efekti yok.
* Diğer varlıkların animasyonları yerinde oynar: hareket (Speed/Accel), animasyon komutları (ses, efekt) ve durum değişiklikleri uygulanmaz.
* Zemin eğimleri ve tetikleyiciler yorumlanmaz.

## 🛠️ Teknolojiler

* **Programlama Dili:** C#
* **Grafik Kütüphanesi:** OpenTK (OpenGL)
* **Hedef Platform:** .NET 10

## ⚙️ Nasıl Çalışır?

`TR2Level` sınıfı, bir `.TR2` dosyasını parametre olarak alır ve `BinaryReader` kullanarak tüm ikili yapıyı belleğe yükler. Değişken uzunluklu geometri verileri (Geometry Word List) işaretçiler (pointer) aracılığıyla taranarak kullanılabilir listelere dönüştürülür.

## 📜 Lisans
Bu proje eğitim ve araştırma amaçlı geliştirilmiştir. Tomb Raider ve ilgili tüm materyallerin hakları ilgili sahiplerine aittir.

## ▶️ Kullanım

Oyun dosyaları telifli olduğu için depoda bulunmaz. Tomb Raider II kurulumunuzdaki `data` klasörünü `TR2Viewer/DATA/` olarak kopyalayın (bu klasör `.gitignore` ile dışarıda tutulur).

```
TR2Viewer.exe                    # Bölüm seçme menüsüyle açılır
TR2Viewer.exe DATA/WALL.TR2      # Belirtilen bölümü açar
```

Menü `DATA` klasöründeki bölümleri oyundaki sırayla ve adlarıyla listeler; bölüm oyun sırasında da Esc ile değiştirilebilir. Arayüz yazıları Windows'un Consolas yazı tipiyle çizildiği için görüntüleyici Windows gerektirir.

Dosya önce çalışma klasöründe, sonra programın klasöründen yukarı doğru aranır; böylece Visual Studio'dan çalıştırıldığında da proje klasöründeki `DATA` bulunur.

Kodda kullanım:
```csharp
using var window = new TRViewer(1920, 1080, "DATA", "DATA/WALL.TR2"); // veri klasörü, ilk bölüm (null = menü)
window.Run();
```

### Kontroller

| Tuş | İşlev |
|---|---|
| W / ↑ | Lara koşar (Shift ile yürür) |
| S / ↓ | Lara geri sıçrar (Shift ile geri yürür) |
| A / D, ← / → | Lara döner (Shift ile yana adım atar) |
| Space | Zıpla (yerinde: yukarı; W/S/A/D ile birlikte: ileri/geri/yana; koşarken: koşarak zıplama) |
| Ctrl | Duvar önünde: 2-3 click yükseğe tırman. Zıplarken basılı tut: kenara tutun. Asılıyken: W yukarı çekil, A/D kenar boyunca kay, Ctrl'yi bırak düş |
| Merdivende (tırmanılabilir duvar) | Önünde dururken Ctrl+W: zıplayıp tutun. Ctrl basılıyken W/S yukarı/aşağı, A/D yana tırman; tepede W ile kenarın üstüne çık, Space ile sırtüstü atla, Ctrl'yi bırak düş. Kenarda asılıyken S: merdivene in |
| Su altında | W/S burun aşağı/yukarı, A/D dön, Space kulaç at |
| Teknede | Space + A/D: sola/sağa atlayarak in (tekneye düşünce veya bölüme teknede başlayınca Lara oturur) |
| Su yüzünde | W/S ileri/geri yüz, A/D dön, Shift+A/D yana yüz, Space'i basılı tut: dal, Ctrl: kenardan sudan çık |
| Fare | Kamerayı Lara'nın etrafında döndürür (serbest kamerada etrafa bakar) |
| N | Lara'yı takip eden kamera ile serbest uçuş kamerası arasında geçiş |
| Serbest kamerada WASD / Space / Sol Shift | Uçuş: ileri, geri, yan, yukarı, aşağı |
| P | Animasyonları durdur / devam ettir |
| Esc | Bölüm seçme menüsü (↑/↓ seç, Enter yükle, Esc geri; listenin sonundaki "Çıkış" programı kapatır) |

