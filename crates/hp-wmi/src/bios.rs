//! `hpqBIntM` transport'u ve salt-okuma tipli getter'lar.

use windows::core::{IUnknown, Interface, BSTR, PCWSTR};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CoSetProxyBlanket, CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED,
    EOAC_NONE, RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE,
};
use windows::Win32::System::Rpc::{RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE};
use windows::Win32::System::Variant::VARIANT;
use windows::Win32::System::Wmi::{
    IWbemClassObject, IWbemLocator, IWbemServices, WbemLocator, WBEM_FLAG_RETURN_WBEM_COMPLETE,
};

use crate::variant;
use crate::HpWmiError;

/// Paylaşılan sır ("SECU").
const SIGN: [u8; 4] = [0x53, 0x45, 0x43, 0x55];
const METHOD_INSTANCE_PATH: &str = r#"hpqBIntM.InstanceName="ACPI\\PNP0C14\\0_0""#;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u32)]
pub enum Cmd {
    Default = 0x20008,
    Keyboard = 0x20009,
    Legacy = 0x00001,
    GpuMode = 0x00002,
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain([0]).collect()
}

pub struct HpWmiBios {
    services: IWbemServices,
    /// `hpqBIntM` sınıf tanımı (method imzaları için).
    method_class: IWbemClassObject,
    /// `Sign` önceden doldurulmuş `hpqBDataIn` şablonu; her çağrıda klonlanır.
    data_template: IWbemClassObject,
}

impl HpWmiBios {
    pub fn new() -> Result<Self, HpWmiError> {
        unsafe {
            // Thread'de COM zaten başlatılmışsa S_FALSE/RPC_E_CHANGED_MODE dönebilir.
            let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

            let locator: IWbemLocator = CoCreateInstance(&WbemLocator, None, CLSCTX_INPROC_SERVER)?;
            let services = locator.ConnectServer(
                &BSTR::from("root\\wmi"),
                &BSTR::new(),
                &BSTR::new(),
                &BSTR::new(),
                0,
                &BSTR::new(),
                None,
            )?;
            CoSetProxyBlanket(
                &services,
                RPC_C_AUTHN_WINNT,
                RPC_C_AUTHZ_NONE,
                None,
                RPC_C_AUTHN_LEVEL_CALL,
                RPC_C_IMP_LEVEL_IMPERSONATE,
                None,
                EOAC_NONE,
            )?;

            let mut method_class = None;
            services.GetObject(
                &BSTR::from("hpqBIntM"),
                WBEM_FLAG_RETURN_WBEM_COMPLETE,
                None,
                Some(&mut method_class),
                None,
            )?;
            let method_class = method_class.ok_or(HpWmiError::NoData)?;

            let mut data_class = None;
            services.GetObject(
                &BSTR::from("hpqBDataIn"),
                WBEM_FLAG_RETURN_WBEM_COMPLETE,
                None,
                Some(&mut data_class),
                None,
            )?;
            let data_template = data_class.ok_or(HpWmiError::NoData)?.SpawnInstance(0)?;
            let sign = variant::from_bytes(&SIGN)?;
            data_template.Put(PCWSTR(wide("Sign").as_ptr()), 0, &sign, 0)?;

            Ok(Self {
                services,
                method_class,
                data_template,
            })
        }
    }

    /// BIOS'a komut gönderir; `out_size` ∈ {0, 4, 128, 1024, 4096}.
    /// Dönüş kodu sıfır değilse `ReturnCode` hatası döner (C# `Send`+`Check`).
    pub fn send(
        &self,
        cmd: Cmd,
        command_type: u32,
        in_data: Option<&[u8]>,
        out_size: usize,
    ) -> Result<Vec<u8>, HpWmiError> {
        let method = match out_size {
            0 => "hpqBIOSInt0",
            4 => "hpqBIOSInt4",
            128 => "hpqBIOSInt128",
            1024 => "hpqBIOSInt1024",
            4096 => "hpqBIOSInt4096",
            other => return Err(HpWmiError::BadOutSize(other)),
        };
        tracing::debug!(?cmd, command_type, in_len = in_data.map_or(0, <[u8]>::len), method, "BIOS çağrısı");

        unsafe {
            // hpqBDataIn örneğini doldur
            let input = self.data_template.Clone()?;
            input.Put(
                PCWSTR(wide("Command").as_ptr()),
                0,
                &variant::from_i32(cmd as u32 as i32),
                0,
            )?;
            input.Put(
                PCWSTR(wide("CommandType").as_ptr()),
                0,
                &variant::from_i32(command_type as i32),
                0,
            )?;
            input.Put(
                PCWSTR(wide("Size").as_ptr()),
                0,
                &variant::from_i32(in_data.map_or(0, <[u8]>::len) as i32),
                0,
            )?;
            if let Some(data) = in_data {
                input.Put(
                    PCWSTR(wide("hpqBData").as_ptr()),
                    0,
                    &variant::from_bytes(data)?,
                    0,
                )?;
            }

            // Method giriş parametrelerini hazırla (InData = hpqBDataIn örneği)
            let mut in_sig = None;
            let mut out_sig = None;
            self.method_class.GetMethod(
                PCWSTR(wide(method).as_ptr()),
                0,
                &mut in_sig,
                &mut out_sig,
            )?;
            let params = in_sig.ok_or(HpWmiError::NoData)?.SpawnInstance(0)?;
            let unknown: IUnknown = input.cast()?;
            params.Put(
                PCWSTR(wide("InData").as_ptr()),
                0,
                &variant::from_unknown(unknown),
                0,
            )?;

            // Çağır
            let mut out_params = None;
            self.services.ExecMethod(
                &BSTR::from(METHOD_INSTANCE_PATH),
                &BSTR::from(method),
                Default::default(),
                None,
                &params,
                Some(&mut out_params),
                None,
            )?;
            let out_params = out_params.ok_or(HpWmiError::NoData)?;

            // OutData nesnesini çöz
            let mut v = VARIANT::default();
            out_params.Get(PCWSTR(wide("OutData").as_ptr()), 0, &mut v, None, None)?;
            let out_obj: IWbemClassObject = variant::to_unknown(&v)?.cast()?;

            let mut rc = VARIANT::default();
            out_obj.Get(PCWSTR(wide("rwReturnCode").as_ptr()), 0, &mut rc, None, None)?;
            let code = variant::to_i32(&rc)?;
            if code != 0 {
                return Err(HpWmiError::ReturnCode(code));
            }

            if out_size == 0 {
                return Ok(Vec::new());
            }
            let mut data = VARIANT::default();
            out_obj.Get(PCWSTR(wide("Data").as_ptr()), 0, &mut data, None, None)?;
            variant::to_bytes(&data)
        }
    }

    // ---- Salt-okuma tipli getter'lar (C# BiosCtl.cs karşılıkları) ----

    /// Sistem tasarım verisi, 128 byte (Cmd.Default 0x28).
    pub fn get_system(&self) -> Result<data::SystemData, HpWmiError> {
        let raw = self.send(Cmd::Default, 0x28, None, 128)?;
        data::SystemData::parse(&raw).ok_or(HpWmiError::NoData)
    }

    /// "Born-on Date", "YYYYMMDD" (Cmd.Legacy 0x10).
    pub fn get_born_date(&self) -> Result<String, HpWmiError> {
        let raw = self.send(Cmd::Legacy, 0x10, None, 128)?;
        Ok(String::from_utf8_lossy(raw.get(..8).ok_or(HpWmiError::NoData)?).into_owned())
    }

    /// Akıllı adaptör durumu (Cmd.Legacy 0x0F).
    pub fn get_adapter(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Legacy, 0x0F, Some(&[0; 4]), 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Grafik modu (Cmd.Legacy 0x52); desteklenmeyen cihazlarda hata döner,
    /// çağıran Hybrid varsaymalı.
    pub fn get_gpu_mode(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Legacy, 0x52, None, 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// GPU güç ayarları (Cmd.Default 0x21).
    pub fn get_gpu_power(&self) -> Result<data::GpuPowerData, HpWmiError> {
        let raw = self.send(Cmd::Default, 0x21, Some(&[0; 4]), 4)?;
        data::GpuPowerData::parse(&raw).ok_or(HpWmiError::NoData)
    }

    /// Fan sayısı (Cmd.Default 0x10).
    pub fn get_fan_count(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Default, 0x10, Some(&[0; 4]), 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Fan tipleri, nibble başına bir fan (Cmd.Default 0x2C).
    pub fn get_fan_type(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Default, 0x2C, Some(&[0; 4]), 128)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Fan hız seviyeleri: (CPU, GPU) (Cmd.Default 0x2D).
    pub fn get_fan_level(&self) -> Result<(u8, u8), HpWmiError> {
        let raw = self.send(Cmd::Default, 0x2D, Some(&[0; 4]), 128)?;
        match raw.as_slice() {
            [a, b, ..] => Ok((*a, *b)),
            _ => Err(HpWmiError::NoData),
        }
    }

    /// Fan hız tablosu (Cmd.Default 0x2F).
    pub fn get_fan_table(&self) -> Result<data::FanTable, HpWmiError> {
        let raw = self.send(Cmd::Default, 0x2F, Some(&[0; 4]), 128)?;
        data::FanTable::parse(&raw).ok_or(HpWmiError::NoData)
    }

    /// Maksimum fan modu açık mı (Cmd.Default 0x26).
    pub fn get_max_fan(&self) -> Result<bool, HpWmiError> {
        Ok(self
            .send(Cmd::Default, 0x26, Some(&[0; 4]), 4)?
            .first()
            .map(|b| b & 1 != 0)
            .ok_or(HpWmiError::NoData)?)
    }

    /// Termal sensör değeri, °C (Cmd.Default 0x23).
    pub fn get_temperature(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Default, 0x23, Some(&[0x01, 0, 0, 0]), 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Klavye aydınlatma durumu (Cmd.Keyboard 0x04).
    pub fn get_backlight(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Keyboard, 0x04, Some(&[0; 4]), 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Klavye renk tablosu (Cmd.Keyboard 0x02).
    pub fn get_color_table(&self) -> Result<data::ColorTable, HpWmiError> {
        let raw = self.send(Cmd::Keyboard, 0x02, Some(&[0; 4]), 128)?;
        data::ColorTable::parse(&raw).ok_or(HpWmiError::NoData)
    }

    /// Klavye tipi (Cmd.Default 0x2B); hata → Standard varsay.
    pub fn get_kbd_type(&self) -> Result<u8, HpWmiError> {
        Ok(*self
            .send(Cmd::Default, 0x2B, Some(&[0; 4]), 4)?
            .first()
            .ok_or(HpWmiError::NoData)?)
    }

    /// Klavye aydınlatması destekleniyor mu (Cmd.Keyboard 0x01).
    pub fn has_backlight(&self) -> Result<bool, HpWmiError> {
        Ok(self
            .send(Cmd::Keyboard, 0x01, Some(&[0; 4]), 4)?
            .first()
            .map(|b| b & 1 != 0)
            .ok_or(HpWmiError::NoData)?)
    }

    /// Tek seferlik yetenek raporu; alan bazında hataya dayanıklı,
    /// hatalar log'a düşer (Victus'ta bazı çağrıların hata vermesi normal).
    pub fn capabilities(&self) -> Capabilities {
        fn logged<T>(name: &str, r: Result<T, HpWmiError>) -> Option<T> {
            r.map_err(|e| tracing::warn!("BIOS {name}: {e}")).ok()
        }
        Capabilities {
            system: logged("GetSystem", self.get_system()),
            born_date: logged("GetBornDate", self.get_born_date()),
            adapter: logged("GetAdapter", self.get_adapter()),
            gpu_mode: logged("GetGpuMode", self.get_gpu_mode()),
            gpu_power: logged("GetGpuPower", self.get_gpu_power()),
            fan_count: logged("GetFanCount", self.get_fan_count()),
            fan_type: logged("GetFanType", self.get_fan_type()),
            fan_table: logged("GetFanTable", self.get_fan_table()),
            kbd_type: logged("GetKbdType", self.get_kbd_type()),
            has_backlight: logged("HasBacklight", self.has_backlight()),
        }
    }
}

use crate::data;

/// Başlangıçta bir kez toplanan BIOS yetenek raporu (C# `GuiFormCaps` verisi).
#[derive(Clone, Debug, Default)]
pub struct Capabilities {
    pub system: Option<data::SystemData>,
    pub born_date: Option<String>,
    pub adapter: Option<u8>,
    pub gpu_mode: Option<u8>,
    pub gpu_power: Option<data::GpuPowerData>,
    pub fan_count: Option<u8>,
    pub fan_type: Option<u8>,
    pub fan_table: Option<data::FanTable>,
    pub kbd_type: Option<u8>,
    pub has_backlight: Option<bool>,
}
