//! WMI property'leri için VARIANT/SAFEARRAY yardımcıları.

use windows::core::IUnknown;
use windows::Win32::System::Ole::{
    SafeArrayAccessData, SafeArrayCreateVector, SafeArrayGetLBound, SafeArrayGetUBound,
    SafeArrayUnaccessData,
};
use windows::Win32::System::Variant::{VARENUM, VARIANT, VT_ARRAY, VT_I4, VT_UI1, VT_UNKNOWN};

use crate::HpWmiError;

/// i32 (WMI uint32 property'leri VT_I4 kabul eder).
pub fn from_i32(value: i32) -> VARIANT {
    VARIANT::from(value)
}

/// VT_ARRAY | VT_UI1 SAFEARRAY'i.
pub fn from_bytes(data: &[u8]) -> Result<VARIANT, HpWmiError> {
    unsafe {
        let psa = SafeArrayCreateVector(VT_UI1, 0, data.len() as u32);
        if psa.is_null() {
            return Err(HpWmiError::NoData);
        }
        let mut p = std::ptr::null_mut();
        SafeArrayAccessData(psa, &mut p)?;
        std::ptr::copy_nonoverlapping(data.as_ptr(), p as *mut u8, data.len());
        SafeArrayUnaccessData(psa)?;

        let mut v = VARIANT::default();
        let v00 = &mut v.Anonymous.Anonymous;
        v00.vt = VARENUM(VT_ARRAY.0 | VT_UI1.0);
        v00.Anonymous.parray = psa;
        Ok(v)
    }
}

/// Gömülü nesne (VT_UNKNOWN).
pub fn from_unknown(unknown: IUnknown) -> VARIANT {
    VARIANT::from(unknown)
}

/// VT_I4/VT_UI4 değeri okur.
pub fn to_i32(v: &VARIANT) -> Result<i32, HpWmiError> {
    unsafe {
        let v00 = &v.Anonymous.Anonymous;
        match v00.vt {
            vt if vt == VT_I4 || vt.0 == 0x13 /* VT_UI4 */ => Ok(v00.Anonymous.lVal),
            _ => Err(HpWmiError::NoData),
        }
    }
}

/// VT_ARRAY | VT_UI1 içeriğini kopyalar.
pub fn to_bytes(v: &VARIANT) -> Result<Vec<u8>, HpWmiError> {
    unsafe {
        let v00 = &v.Anonymous.Anonymous;
        if v00.vt.0 & VT_ARRAY.0 == 0 {
            return Err(HpWmiError::NoData);
        }
        let psa = v00.Anonymous.parray;
        if psa.is_null() {
            return Err(HpWmiError::NoData);
        }
        let lb = SafeArrayGetLBound(psa, 1)?;
        let ub = SafeArrayGetUBound(psa, 1)?;
        let len = (ub - lb + 1).max(0) as usize;
        let mut p = std::ptr::null_mut();
        SafeArrayAccessData(psa, &mut p)?;
        let out = std::slice::from_raw_parts(p as *const u8, len).to_vec();
        SafeArrayUnaccessData(psa)?;
        Ok(out)
    }
}

/// VT_UNKNOWN değerini klonlar.
pub fn to_unknown(v: &VARIANT) -> Result<IUnknown, HpWmiError> {
    unsafe {
        let v00 = &v.Anonymous.Anonymous;
        if v00.vt != VT_UNKNOWN {
            return Err(HpWmiError::NoData);
        }
        (*v00.Anonymous.punkVal).clone().ok_or(HpWmiError::NoData)
    }
}
