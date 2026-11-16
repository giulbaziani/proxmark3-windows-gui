# Proxmark3 — დაყენება და გაშვება (ორიგინალი გზამკვლევი)

> ეს არის დეველოპერის ორიგინალი ქართული ინსტრუქცია, რომელსაც ეს აპლიკაცია ავტომატიზირებს.
> აპლიკაციის გამოყენებისას ამ ნაბიჯების ხელით გავლა საჭირო **აღარ არის** — იხ. [USER-GUIDE.md](USER-GUIDE.md).

Proxmark3 — დაყენება და გაშვება
Nov 15, 2026 · @Rembo
მოკლე პრაქტიკული გზამკვლევი Proxmark3 Easy-სთვის (256KB) Windows + WSL2 გარემოში — დაყენებიდან ბარათის კლონამდე.
ერთჯერადი დაყენება
ეს ნაბიჯები ერთხელ კეთდება.
1. WSL + Ubuntu — Windows-ის ადმინისტრატორის cmd-ში:
wsl --install -d Ubuntuwsl --set-default Ubuntu
Ubuntu მოგთხოვს მომხმარებლის სახელსა და პაროლს — დაიმახსოვრე, sudo-სთვის დაგჭირდება.
2. საჭირო პაკეტები — Ubuntu-ში:
sudo apt updatesudo apt install -y git build-essential pkg-config libreadline-dev gcc-arm-none-eabi libnewlib-dev qtbase5-dev libbz2-dev liblz4-dev libbluetooth-dev libpython3-dev libssl-dev libgd-dev
3. კოდი + 256KB კონფიგი — Easy-ს მხოლოდ 256KB flash აქვს, ამიტომ იშვიათი მოდულები ამოღებულია:
cd ~git clone https://github.com/RfidResearchGroup/proxmark3.gitcd proxmark3cat > Makefile.platform << 'EOF'PLATFORM=PM3GENERICPLATFORM_SIZE=256LED_ORDER=PM3EASYSTANDALONE=SKIP_HITAG=1SKIP_EM4x50=1SKIP_EM4x70=1SKIP_ZX8211=1SKIP_LEGICRF=1SKIP_FELICA=1SKIP_NFCBARCODE=1EOF
4. კომპილაცია:
make clean && make -j
5. firmware-ის ჩაწერა — Proxmark3 USB-ით მიერთებული:
sudo ./pm3-flash-all
ბოლოს Have a nice day! უნდა გამოჩნდეს, შეცდომების გარეშე.
გაშვება (ყოველ ჯერზე)
USB მოწყობილობა WSL-ს უნდა მიედეს usbipd-ით, შემდეგ Ubuntu-ში ირთვება pm3.
1. USB-ის WSL-ზე მიერთება — Windows-ის ადმინ cmd-ში (ეს ფანჯარა ღია დატოვე):
usbipd attach --auto-attach --busid 2-1 --wsl
2-1-ის ნაცვლად შენი busid-ი — usbipd list-ით ნახავ. თუ პირველად აკეთებ, წინ ერთხელ გაუშვი: usbipd bind --busid 2-1.
2. Ubuntu-ში გაშვება:
wslcd ~/proxmark3sudo ./pm3
როდესაც [usb] pm3 --> გამოცნდება — მზადაა. გამოსასვლელად აკრიფე quit.
შენიშვნა: firmware-ის ჩაწერის შემდეგ მოწყობილობას ახალი USB ID აქვს — თუ pm3-მ ვერ იპოვა, usbipd-ს bind + attach ხელახლა გაუშვი.
ძირითადი ბრძანებები
ბრძანება
რას აკეთებს
hf search
13.56 MHz ბარათის ამოცნობა (UID, ტიპი, magic)
lf search
125 kHz ბარათის ამოცნობა (EM4100, HID…)
hf mf info
Mifare-ის დეტალური ინფო, magic ტიპი, გასაღებები
hf mf autopwn
Mifare გასაღებების პოვნა + სრული დამპი
hf mf rdbl --blk N -k KEY
ერთი ბლოკის წაკითხვა
hf mf wrbl --blk N -k KEY -d DATA --force
ერთი ბლოკის ჩაწერა
hf mf restore -f FILE --force
დამპის ჩაწერა CUID-ზე (სრული კლონი)
hf mf cload -f FILE
დამპის ჩაწერა მხოლოდ Gen1a-ზე
hf mf view -f FILE
შენახული დამპის ნახვა (ბარათის გარეშე)
lf em 410x clone --id ID
EM4100-ის ჩაწერა T5577-ზე
lf t55xx detect
T5577 ჩიპის ამოცნობა (ცარიელი = ცუდი კავშირი)
hw tune
ანტენების შემოწმება (LF/HF ძაბა)
hw version
კლიენტისა და firmware-ის ვერსია
quit
პროგრამიდან გამოსვლა
ბარათის კლონირება
კლონირება იწყება წყაროს (ორიგინალი) წაკითხვით და ჩასაწერი ბარათის (სამიზნის) გარკვევით. მოცილება: წყარო და სამიზნე ერთი სიხშირის უნდა იყოს.
Mifare — 13.56 MHz
წყარო ბარათი HF ანტენაზე (პლატაზე დახაზული ბრტყელი ხვია) → hf mf autopwn (ინახავს დამპს).
სამიზნე magic ბარათი ანტენაზე → hf mf restore -f hf-mf-<UID>-dump.bin --force.
შემოწმება → hf mf rdbl --blk 0 -k FFFFFFFFFFFF (UID იდენტური უნდა იყოს).
EM4100 — 125 kHz
წყარო LF ანტენაზე (მავთულის რგოლი) → lf search, დაიმახსოვრე ID.
სამიზნე T5577 ბარათი → lf em 410x clone --id <ID>.
შემოწმება → lf search (ახალი ID უნდა აჩვენოს).
magic ბარათების ტიპები (რომელი იყიდო)
ტიპი
UID ჩაწერა
როგორ
Gen1a (UID)
მრავალჯერადი
cload; იაფი, მაგრამ ზოგ რიდერი ცნობს
Gen2 / CUID
მრავალჯერადი
restore --force; გირჩევია — ნაკლებად ცნობადი
FUID / FNUID
მხოლოდ ერთხელ
შეცდომის უფლება არ აქვს — მოერიდე
Fudan FM11RF08
— (არა-magic)
UID არ იცვლება
სრული კლონისთვის (UID-ის ჩათვლით) იყიდე Gen1a ან Gen2/CUID.
ხშირი პრობლემები
პრობლემა
მიზეზი / გამოსავალი
make: command not found
ProxSpace გამოტანე, WSL გზა აირჩიე. Ubuntu-ში ყველა პაკეტი დააყენე (apt install).
Firmware image too large
firmware 256KB-ში არ ეტევა. Makefile.platform-ში მეტი SKIP_* დაამატე (მაგ. SKIP_ICLASS=1, SKIP_ISO15693=1).
invalid serial port /dev/ttyACM0
მოწყობილობა WSL-ს არ ახედავს. usbipd attach ხელახლა გაუშვი; ან უფლება — sudo ./pm3.
Proxmark3 not found ფლეშვისას
ChAწერისას მოწყობილობა გადაიტვირთა. ღილაკზე დაჭერით გართე (bootloader) და გაუშვი ./pm3-flash-all.
cload: wupC1 error
არა-Gen1a ბარათზე cload არ მუშაობს. CUID-ზე გამოიყენე restore --force.
წაცერის შემდეგ UID/კოდი არ იცვლება
ანტენასთან ორი ბარათია. მოაცილე ყველა და მხოლოდ ერთი დაატოვე.
რეალურმა რიდერმა არ მიიღო კლონი
სისტემა სპეც-გასაღებს ამოწმებს (მაგ. სექტორ 15). ხელით დააბრუნე — hf mf wrbl --blk 63 ....
lf t55xx detect ვერ მუშაობს
ბარათი read-only EM4100-ია (არა T5577) — არ იწერება. ან პოზიცია ცუდია.
