%global _libdir /usr/lib
Name:           bookshelfng
Version:        0.0.0
Release:        1%{?dist}
Summary:        Standalone book library manager and download automation server
License:        GPL-3.0-or-later
URL:            https://github.com/snapetech/bookshelfng
Source0:        bookshelfng-%{version}.tar.gz
BuildArch:      x86_64
Requires:       glibc
Requires:       libgcc
Requires:       libicu
Requires:       openssl-libs
Requires:       sqlite-libs
Requires(post): systemd
Requires(preun): systemd
Requires(postun): systemd

%description
BookshelfNG is a standalone book library manager with author and book metadata,
library monitoring, release search, and download automation.

%prep
%setup -q -c -T
tar -xzf %{SOURCE0} --strip-components=1 -C .

%build

%install
install -d %{buildroot}/usr/lib/bookshelfng
install -d %{buildroot}%{_bindir}
install -d %{buildroot}%{_sysconfdir}/bookshelfng
install -d %{buildroot}%{_unitdir}
install -d %{buildroot}%{_prefix}/lib/sysusers.d
install -d %{buildroot}%{_prefix}/lib/tmpfiles.d
cp -a ./. %{buildroot}/usr/lib/bookshelfng/
ln -s /usr/lib/bookshelfng/Readarr %{buildroot}%{_bindir}/bookshelfng
install -m 0644 %{_sourcedir}/bookshelfng.service %{buildroot}%{_unitdir}/bookshelfng.service
install -m 0644 %{_sourcedir}/bookshelfng.env %{buildroot}%{_sysconfdir}/bookshelfng/bookshelfng.env
install -m 0644 %{_sourcedir}/bookshelfng.sysusers %{buildroot}%{_prefix}/lib/sysusers.d/bookshelfng.conf
install -m 0644 %{_sourcedir}/bookshelfng.tmpfiles %{buildroot}%{_prefix}/lib/tmpfiles.d/bookshelfng.conf
chmod -R u=rwX,go=rX %{buildroot}/usr/lib/bookshelfng

%pre
getent group bookshelfng >/dev/null || groupadd --system bookshelfng
getent passwd bookshelfng >/dev/null || useradd --system --gid bookshelfng --home-dir /var/lib/bookshelfng --shell /sbin/nologin bookshelfng

%post
systemctl daemon-reload >/dev/null 2>&1 || :
systemd-sysusers >/dev/null 2>&1 || :
systemd-tmpfiles --create /usr/lib/tmpfiles.d/bookshelfng.conf >/dev/null 2>&1 || :

%preun
if [ "$1" -eq 0 ]; then
  systemctl disable --now bookshelfng.service >/dev/null 2>&1 || :
fi

%postun
systemctl daemon-reload >/dev/null 2>&1 || :

%files
%license LICENSE.md
%config(noreplace) %{_sysconfdir}/bookshelfng/bookshelfng.env
%{_bindir}/bookshelfng
/usr/lib/bookshelfng/
%{_unitdir}/bookshelfng.service
%{_prefix}/lib/sysusers.d/bookshelfng.conf
%{_prefix}/lib/tmpfiles.d/bookshelfng.conf

%changelog
* Thu Jan 01 1970 BookshelfNG Release Automation <bookshelfng@snapetech.com> - 0.0.0-1
- Initial package
